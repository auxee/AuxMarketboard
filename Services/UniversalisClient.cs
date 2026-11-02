using System.Net;
using System.Net.Http;
using System.Text.Json;

namespace AuxMarketboard.Services;

public sealed class UniversalisClient : IDisposable
{
    public const int ChunkSize = 100;
    public const int FirstPassListings = 100;

    private const int MaxConcurrency = 4;
    private const int MaxAttempts = 5;
    private const double FallbackTaxRate = 0.05;
    private static readonly TimeSpan MinGapBetweenRequests = TimeSpan.FromMilliseconds(60);
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan MaxServerDelay = TimeSpan.FromSeconds(60);
    private static readonly Uri BaseUri = new("https://universalis.app/api/v2/");

    private const string MultiItemFields =
        "items.listings.pricePerUnit,items.listings.quantity,items.listings.hq,items.listings.worldName,"
        + "items.listings.total,items.listings.tax,items.listings.lastReviewTime,items.lastUploadTime";

    private const string SingleItemFields =
        "listings.pricePerUnit,listings.quantity,listings.hq,listings.worldName,"
        + "listings.total,listings.tax,listings.lastReviewTime,lastUploadTime";

    private readonly HttpClient httpClient;
    private readonly SemaphoreSlim concurrency = new(MaxConcurrency);
    private readonly object slotLock = new();
    private DateTime nextSlotUtc = DateTime.MinValue;
    private long rateLimitedUntilTicks;

    public UniversalisClient(string version)
    {
        httpClient = new HttpClient
        {
            BaseAddress = BaseUri,
            Timeout = TimeSpan.FromSeconds(30),
        };
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd($"AuxMarketboard/{version}");
    }

    public bool IsRateLimited => DateTime.UtcNow.Ticks < Interlocked.Read(ref rateLimitedUntilTicks);

    public async Task<IReadOnlyList<DataCenterInfo>> GetDataCentersAsync(CancellationToken cancellationToken)
    {
        var bytes = await GetBytesAsync("data-centers", cancellationToken).ConfigureAwait(false);
        using var document = JsonDocument.Parse(bytes);
        var result = new List<DataCenterInfo>();
        foreach (var element in document.RootElement.EnumerateArray())
        {
            var name = ReadString(element, "name");
            var region = ReadString(element, "region");
            var worlds = new List<uint>();
            if (element.TryGetProperty("worlds", out var worldArray) && worldArray.ValueKind == JsonValueKind.Array)
            {
                foreach (var world in worldArray.EnumerateArray())
                {
                    if (world.TryGetUInt32(out var id))
                    {
                        worlds.Add(id);
                    }
                }
            }

            if (name.Length > 0)
            {
                result.Add(new DataCenterInfo(name, region, worlds));
            }
        }

        return result;
    }

    public async Task<IReadOnlyList<WorldInfo>> GetWorldsAsync(CancellationToken cancellationToken)
    {
        var bytes = await GetBytesAsync("worlds", cancellationToken).ConfigureAwait(false);
        using var document = JsonDocument.Parse(bytes);
        var result = new List<WorldInfo>();
        foreach (var element in document.RootElement.EnumerateArray())
        {
            var name = ReadString(element, "name");
            if (name.Length > 0 && element.TryGetProperty("id", out var idElement) && idElement.TryGetUInt32(out var id))
            {
                result.Add(new WorldInfo(id, name));
            }
        }

        return result;
    }

    public async Task<IReadOnlyDictionary<uint, ItemMarket>> GetMarketAsync(
        string scope,
        string defaultWorld,
        IReadOnlyCollection<uint> itemIds,
        int listingsCap,
        int chunkSize,
        Action? onChunkDone,
        CancellationToken cancellationToken)
    {
        var results = new Dictionary<uint, ItemMarket>();
        var chunks = itemIds.Distinct().Chunk(Math.Clamp(chunkSize, 1, ChunkSize)).ToList();
        if (chunks.Count == 0)
        {
            return results;
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = linked.Token;
        var tasks = chunks.Select(async chunk =>
        {
            try
            {
                var markets = await GetChunkAsync(scope, defaultWorld, chunk, listingsCap, token).ConfigureAwait(false);
                lock (results)
                {
                    foreach (var market in markets)
                    {
                        results[market.ItemId] = market;
                    }
                }

                onChunkDone?.Invoke();
            }
            catch
            {
                linked.Cancel();
                throw;
            }
        }).ToList();

        try
        {
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
        catch
        {
            cancellationToken.ThrowIfCancellationRequested();
            var failure = tasks.FirstOrDefault(x => x.IsFaulted)?.Exception?.GetBaseException();
            if (failure is not null)
            {
                throw failure;
            }

            throw;
        }

        return results;
    }

    private async Task<List<ItemMarket>> GetChunkAsync(string scope, string defaultWorld, uint[] chunk, int listingsCap, CancellationToken cancellationToken)
    {
        var single = chunk.Length == 1;
        var query = listingsCap > 0 ? $"listings={listingsCap}&entries=0&fields=" : "entries=0&fields=";
        query += single ? SingleItemFields : MultiItemFields;
        var url = $"{Uri.EscapeDataString(scope)}/{string.Join(",", chunk)}?{query}";

        var bytes = await GetBytesAsync(url, cancellationToken).ConfigureAwait(false);
        using var document = JsonDocument.Parse(bytes);
        var root = document.RootElement;
        var markets = new List<ItemMarket>();

        if (root.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in items.EnumerateObject())
            {
                if (uint.TryParse(property.Name, out var id))
                {
                    markets.Add(ParseItem(id, property.Value, defaultWorld, listingsCap));
                }
            }
        }
        else if (single)
        {
            markets.Add(ParseItem(chunk[0], root, defaultWorld, listingsCap));
        }

        return markets;
    }

    public static ItemMarket ParseItem(uint itemId, JsonElement element, string defaultWorld, int listingsCap)
    {
        var listings = new List<MarketListing>();
        if (element.TryGetProperty("listings", out var array) && array.ValueKind == JsonValueKind.Array)
        {
            foreach (var entry in array.EnumerateArray())
            {
                var unitPrice = ReadInt(entry, "pricePerUnit");
                var quantity = ReadInt(entry, "quantity");
                if (quantity <= 0)
                {
                    continue;
                }

                var total = ReadLong(entry, "total");
                if (total <= 0)
                {
                    total = (long)unitPrice * quantity;
                }

                var tax = entry.TryGetProperty("tax", out var taxElement) && taxElement.TryGetInt64(out var parsedTax)
                    ? parsedTax
                    : (long)Math.Ceiling(total * FallbackTaxRate);
                var world = ReadString(entry, "worldName");
                var hq = entry.TryGetProperty("hq", out var hqElement) && hqElement.ValueKind == JsonValueKind.True;
                listings.Add(new MarketListing(world.Length > 0 ? world : defaultWorld, unitPrice, quantity, total + tax, hq));
            }
        }

        var uploaded = ReadLong(element, "lastUploadTime");
        var complete = listingsCap <= 0 || listings.Count < listingsCap;
        return new ItemMarket(itemId, listings, uploaded, complete);
    }

    private async Task<byte[]> GetBytesAsync(string url, CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            HttpStatusCode? status = null;
            TimeSpan? serverDelay = null;
            Exception? failure = null;

            await concurrency.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await WaitForSlotAsync(cancellationToken).ConfigureAwait(false);
                using var response = await httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
                status = response.StatusCode;
                if (response.IsSuccessStatusCode)
                {
                    return await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
                }

                serverDelay = response.Headers.RetryAfter?.Delta
                    ?? (response.Headers.RetryAfter?.Date is { } date ? date - DateTimeOffset.UtcNow : null);
            }
            catch (HttpRequestException exception)
            {
                failure = exception;
            }
            catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
            {
                failure = exception;
            }
            finally
            {
                concurrency.Release();
            }

            var retryable = status is null || status == HttpStatusCode.TooManyRequests || (int)status.Value >= 500;
            if (!retryable || attempt >= MaxAttempts - 1)
            {
                if (failure is not null)
                {
                    throw failure;
                }

                throw new HttpRequestException($"Universalis returned {(int)status!.Value}", null, status);
            }

            var delay = serverDelay is { } hinted && hinted > TimeSpan.Zero
                ? (hinted > MaxServerDelay ? MaxServerDelay : hinted)
                : TimeSpan.FromSeconds(Math.Min(Math.Pow(2, attempt), MaxBackoff.TotalSeconds));

            if (status == HttpStatusCode.TooManyRequests)
            {
                Interlocked.Exchange(ref rateLimitedUntilTicks, (DateTime.UtcNow + delay).Ticks);
            }

            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task WaitForSlotAsync(CancellationToken cancellationToken)
    {
        TimeSpan wait;
        lock (slotLock)
        {
            var now = DateTime.UtcNow;
            var slot = nextSlotUtc > now ? nextSlotUtc : now;
            nextSlotUtc = slot + MinGapBetweenRequests;
            wait = slot - now;
        }

        if (wait > TimeSpan.Zero)
        {
            await Task.Delay(wait, cancellationToken).ConfigureAwait(false);
        }
    }

    private static string ReadString(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : string.Empty;

    private static int ReadInt(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.TryGetInt32(out var parsed) ? parsed : 0;

    private static long ReadLong(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.TryGetInt64(out var parsed) ? parsed : 0;

    public void Dispose()
    {
        httpClient.Dispose();
        concurrency.Dispose();
    }
}
