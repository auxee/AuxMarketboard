namespace AuxMarketboard.Services;

public sealed class MarketService : IDisposable
{
    public const int ListChangeDebounceMilliseconds = 700;

    private static readonly TimeSpan DisposeWait = TimeSpan.FromSeconds(2);

    private readonly Configuration configuration;
    private readonly UniversalisClient client;
    private readonly WorldDirectory directory;
    private readonly Func<HomeWorld?> homeProvider;
    private readonly Action<string, Exception> logError;
    private readonly object gate = new();

    private readonly CancellationTokenSource lifetime = new();
    private CancellationTokenSource? current;
    private int generation;
    private int inFlight;
    private volatile MarketSnapshot snapshot = MarketSnapshot.Empty;
    private volatile RefreshState state = RefreshState.Idle;
    private volatile ScopeTarget scope = ScopeTarget.Unknown;
    private string lastScopeKey = string.Empty;

    public MarketService(
        Configuration configuration,
        UniversalisClient client,
        WorldDirectory directory,
        Func<HomeWorld?> homeProvider,
        Action<string, Exception> logError)
    {
        this.configuration = configuration;
        this.client = client;
        this.directory = directory;
        this.homeProvider = homeProvider;
        this.logError = logError;
    }

    public MarketSnapshot Snapshot => snapshot;

    public RefreshState State => state;

    public ScopeTarget Scope => scope;

    public bool IsRateLimited => client.IsRateLimited;

    public IReadOnlyList<ShoppingEntry> Items => configuration.Items;

    public ScopeMode Mode => configuration.ScopeMode;

    public int MaxWorlds => configuration.MaxWorlds;

    public string DataCenterOf(string world) => directory.DataCenterOf(world);

    public string HomeWorldName => scope.World;

    public void StartDirectoryLoad()
    {
        Track(() => directory.LoadAsync(client, lifetime.Token));
    }

    public void Tick()
    {
        var resolved = ResolveScope();
        scope = resolved;

        if (resolved.Key != lastScopeKey)
        {
            lastScopeKey = resolved.Key;
            if (resolved.IsKnown && configuration.Items.Count > 0)
            {
                Refresh();
            }
        }

        if (directory.ShouldRetry)
        {
            StartDirectoryLoad();
        }
    }

    public void SetScopeMode(ScopeMode mode)
    {
        if (configuration.ScopeMode == mode)
        {
            return;
        }

        configuration.ScopeMode = mode;
        configuration.Save();
    }

    public void SetMaxWorlds(int maxWorlds)
    {
        var clamped = Math.Clamp(maxWorlds, 0, Configuration.MaxWorldLimit);
        if (configuration.MaxWorlds == clamped)
        {
            return;
        }

        configuration.MaxWorlds = clamped;
        configuration.Save();
        Request(false, 0);
    }

    public void AddItem(uint itemId, int quantity)
    {
        AddItems(new[] { new ImportedItem(itemId, quantity) });
    }

    public void AddItems(IEnumerable<ImportedItem> items)
    {
        var changed = false;
        foreach (var item in items)
        {
            if (item.ItemId == 0 || item.Quantity <= 0)
            {
                continue;
            }

            var existing = configuration.Items.FirstOrDefault(x => x.ItemId == item.ItemId);
            if (existing is null)
            {
                configuration.Items.Add(new ShoppingEntry
                {
                    ItemId = item.ItemId,
                    Quantity = Math.Clamp(item.Quantity, 1, Configuration.MaxQuantity),
                });
            }
            else
            {
                existing.Quantity = (int)Math.Clamp((long)existing.Quantity + item.Quantity, 1, Configuration.MaxQuantity);
            }

            changed = true;
        }

        if (changed)
        {
            ListChanged();
        }
    }

    public void SetQuantity(ShoppingEntry entry, int quantity)
    {
        var clamped = Math.Clamp(quantity, 1, Configuration.MaxQuantity);
        if (entry.Quantity == clamped)
        {
            return;
        }

        entry.Quantity = clamped;
        ListChanged();
    }

    public void SetHqOnly(ShoppingEntry entry, bool hqOnly)
    {
        if (entry.HqOnly == hqOnly)
        {
            return;
        }

        entry.HqOnly = hqOnly;
        ListChanged();
    }

    public void Remove(uint itemId)
    {
        if (configuration.Items.RemoveAll(x => x.ItemId == itemId) > 0)
        {
            ListChanged();
        }
    }

    public void Clear()
    {
        if (configuration.Items.Count == 0)
        {
            return;
        }

        configuration.Items.Clear();
        ListChanged();
    }

    public void Refresh()
    {
        Request(true, 0);
    }

    public void Dispose()
    {
        lifetime.Cancel();
        lock (gate)
        {
            generation++;
            current?.Cancel();
        }

        var deadline = DateTime.UtcNow + DisposeWait;
        while (Volatile.Read(ref inFlight) > 0 && DateTime.UtcNow < deadline)
        {
            Thread.Sleep(10);
        }

        if (Volatile.Read(ref inFlight) == 0)
        {
            lifetime.Dispose();
        }
    }

    private void Track(Func<Task> work)
    {
        Interlocked.Increment(ref inFlight);
        _ = Task.Run(async () =>
        {
            try
            {
                await work().ConfigureAwait(false);
            }
            finally
            {
                Interlocked.Decrement(ref inFlight);
            }
        });
    }

    private void ListChanged()
    {
        configuration.Save();
        Request(false, ListChangeDebounceMilliseconds);
    }

    private ScopeTarget ResolveScope()
    {
        var home = homeProvider();
        var world = configuration.LastWorld;
        var dataCenter = configuration.LastDataCenter;
        var region = configuration.LastRegion;

        if (home is not null && home.Name.Length > 0)
        {
            if (directory.TryFind(home.Name, home.Id, out var place))
            {
                world = place.World;
                dataCenter = place.DataCenter;
                region = place.Region;
            }
            else
            {
                var sameDataCenter = string.Equals(configuration.LastDataCenter, home.DataCenter, StringComparison.Ordinal);
                world = home.Name;
                dataCenter = home.DataCenter;
                region = sameDataCenter ? configuration.LastRegion : string.Empty;
            }

            if (world != configuration.LastWorld || dataCenter != configuration.LastDataCenter || region != configuration.LastRegion)
            {
                configuration.LastWorld = world;
                configuration.LastDataCenter = dataCenter;
                configuration.LastRegion = region;
                configuration.Save();
            }
        }

        return new ScopeTarget(configuration.ScopeMode, world, dataCenter, region);
    }

    private void Request(bool force, int delayMilliseconds)
    {
        var target = scope;
        if (!target.IsKnown)
        {
            return;
        }

        var entries = configuration.Items
            .Select(x => new ShoppingEntry { ItemId = x.ItemId, Quantity = x.Quantity, HqOnly = x.HqOnly })
            .ToArray();
        var maxWorlds = configuration.MaxWorlds;

        CancellationTokenSource source;
        int myGeneration;
        lock (gate)
        {
            current?.Cancel();
            source = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            current = source;
            myGeneration = ++generation;
        }

        if (entries.Length == 0)
        {
            ReleaseSource(source);
            Commit(myGeneration, RefreshState.Idle, new MarketSnapshot(
                target.Key,
                target.Name,
                DateTime.UtcNow,
                new Dictionary<uint, ItemMarket>(),
                BuyPlan.Empty,
                new Dictionary<uint, (int, bool)>()));
            return;
        }

        SetState(myGeneration, new RefreshState(RefreshPhase.Waiting, 0, 0, string.Empty));
        Track(() => RunAsync(entries, target, force, maxWorlds, delayMilliseconds, myGeneration, source));
    }

    private void ReleaseSource(CancellationTokenSource source)
    {
        lock (gate)
        {
            if (ReferenceEquals(current, source))
            {
                current = null;
            }
        }

        source.Dispose();
    }

    private async Task RunAsync(
        ShoppingEntry[] entries,
        ScopeTarget target,
        bool force,
        int maxWorlds,
        int delayMilliseconds,
        int myGeneration,
        CancellationTokenSource source)
    {
        var cancellationToken = source.Token;
        try
        {
            if (delayMilliseconds > 0)
            {
                await Task.Delay(delayMilliseconds, cancellationToken).ConfigureAwait(false);
            }

            var previous = snapshot;
            var markets = new Dictionary<uint, ItemMarket>();
            if (!force && previous.ScopeKey == target.Key)
            {
                foreach (var pair in previous.Markets)
                {
                    markets[pair.Key] = pair.Value;
                }
            }

            var wanted = entries.Select(x => x.ItemId).Distinct().ToList();
            var missing = wanted.Where(x => !markets.ContainsKey(x)).ToList();
            var chunksTotal = (missing.Count + UniversalisClient.ChunkSize - 1) / UniversalisClient.ChunkSize;
            var chunksDone = 0;

            void ChunkDone()
            {
                var done = Interlocked.Increment(ref chunksDone);
                SetState(myGeneration, new RefreshState(RefreshPhase.Fetching, done, Volatile.Read(ref chunksTotal), string.Empty));
            }

            if (missing.Count > 0)
            {
                SetState(myGeneration, new RefreshState(RefreshPhase.Fetching, 0, chunksTotal, string.Empty));
                var fetched = await client.GetMarketAsync(
                    target.Name,
                    target.DefaultWorld,
                    missing,
                    UniversalisClient.FirstPassListings,
                    UniversalisClient.ChunkSize,
                    ChunkDone,
                    cancellationToken).ConfigureAwait(false);
                foreach (var id in missing)
                {
                    markets[id] = fetched.TryGetValue(id, out var market) ? market : ItemMarket.None(id);
                }
            }

            async Task FetchUncapped(List<uint> ids)
            {
                Interlocked.Add(ref chunksTotal, ids.Count);
                SetState(myGeneration, new RefreshState(RefreshPhase.Fetching, Volatile.Read(ref chunksDone), Volatile.Read(ref chunksTotal), string.Empty));
                var fetched = await client.GetMarketAsync(
                    target.Name,
                    target.DefaultWorld,
                    ids,
                    0,
                    1,
                    ChunkDone,
                    cancellationToken).ConfigureAwait(false);
                foreach (var pair in fetched)
                {
                    markets[pair.Key] = pair.Value;
                }
            }

            BuyPlan BuildPlan()
            {
                var requests = entries
                    .Select(x => new PricingRequest(
                        x.ItemId,
                        x.Quantity,
                        x.HqOnly,
                        markets.TryGetValue(x.ItemId, out var market) ? market.Listings : Array.Empty<MarketListing>()))
                    .ToList();
                return PricingEngine.BuildPlan(requests, maxWorlds, directory.DataCenterOf);
            }

            var truncated = entries
                .Where(x => markets.TryGetValue(x.ItemId, out var market)
                    && !market.Complete
                    && EligibleUnits(market, x.HqOnly) < x.Quantity)
                .Select(x => x.ItemId)
                .Distinct()
                .ToList();

            if (truncated.Count > 0)
            {
                await FetchUncapped(truncated).ConfigureAwait(false);
            }

            var plan = BuildPlan();
            if (maxWorlds > 0)
            {
                var shortIncomplete = plan.Items
                    .Where(x => x.Short > 0 && markets.TryGetValue(x.ItemId, out var market) && !market.Complete)
                    .Select(x => x.ItemId)
                    .Distinct()
                    .ToList();
                if (shortIncomplete.Count > 0)
                {
                    await FetchUncapped(shortIncomplete).ConfigureAwait(false);
                    plan = BuildPlan();
                }
            }

            var inputs = entries.ToDictionary(x => x.ItemId, x => (x.Quantity, x.HqOnly));

            cancellationToken.ThrowIfCancellationRequested();
            Commit(myGeneration, RefreshState.Idle, new MarketSnapshot(target.Key, target.Name, DateTime.UtcNow, markets, plan, inputs));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (lifetime.IsCancellationRequested)
            {
                return;
            }

            logError("Universalis refresh failed", exception);
            SetState(myGeneration, new RefreshState(RefreshPhase.Failed, 0, 0, Describe(exception)));
        }
        finally
        {
            ReleaseSource(source);
        }
    }

    private static long EligibleUnits(ItemMarket market, bool hqOnly)
    {
        long units = 0;
        foreach (var listing in market.Listings)
        {
            if (!hqOnly || listing.Hq)
            {
                units += listing.Quantity;
            }
        }

        return units;
    }

    private static string Describe(Exception exception)
    {
        if (exception is System.Net.Http.HttpRequestException { StatusCode: { } status })
        {
            return $"Universalis error {(int)status}";
        }

        return exception is System.Net.Http.HttpRequestException ? "Could not reach Universalis" : exception.Message;
    }

    private void SetState(int forGeneration, RefreshState newState)
    {
        lock (gate)
        {
            if (forGeneration == generation)
            {
                state = newState;
            }
        }
    }

    private void Commit(int forGeneration, RefreshState newState, MarketSnapshot newSnapshot)
    {
        lock (gate)
        {
            if (forGeneration != generation)
            {
                return;
            }

            snapshot = newSnapshot;
            state = newState;
        }
    }
}
