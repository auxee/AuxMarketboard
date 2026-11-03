namespace AuxMarketboard.Services;

public sealed record HomeWorld(uint Id, string Name, string DataCenter);

public sealed record ScopeTarget(ScopeMode Mode, string World, string DataCenter, string Region)
{
    public static readonly ScopeTarget Unknown = new(ScopeMode.Region, string.Empty, string.Empty, string.Empty);

    public string Name
    {
        get
        {
            var preferred = Mode switch
            {
                ScopeMode.World => World,
                ScopeMode.DataCenter => DataCenter,
                _ => Region,
            };

            if (!string.IsNullOrEmpty(preferred))
            {
                return preferred;
            }

            return !string.IsNullOrEmpty(DataCenter) ? DataCenter : World;
        }
    }

    public bool IsKnown => Name.Length > 0;

    public string Key => $"{Mode}:{Name}";

    public string DefaultWorld => Name == World ? World : string.Empty;
}

public enum RefreshPhase
{
    Idle,
    Waiting,
    Fetching,
    Failed,
}

public sealed record RefreshState(RefreshPhase Phase, int ChunksDone, int ChunksTotal, string Error)
{
    public static readonly RefreshState Idle = new(RefreshPhase.Idle, 0, 0, string.Empty);
}

public sealed class MarketSnapshot
{
    public static readonly MarketSnapshot Empty = new(
        string.Empty,
        string.Empty,
        DateTime.MinValue,
        new Dictionary<uint, ItemMarket>(),
        BuyPlan.Empty,
        new Dictionary<uint, (int, bool)>());

    public MarketSnapshot(
        string scopeKey,
        string scopeName,
        DateTime fetchedUtc,
        IReadOnlyDictionary<uint, ItemMarket> markets,
        BuyPlan plan,
        IReadOnlyDictionary<uint, (int Quantity, bool HqOnly)> inputs)
    {
        ScopeKey = scopeKey;
        ScopeName = scopeName;
        FetchedUtc = fetchedUtc;
        Markets = markets;
        Plan = plan;
        Inputs = inputs;
        CheapestById = plan.CheapestItems.ToDictionary(x => x.ItemId);
    }

    public string ScopeKey { get; }
    public string ScopeName { get; }
    public DateTime FetchedUtc { get; }
    public IReadOnlyDictionary<uint, ItemMarket> Markets { get; }
    public BuyPlan Plan { get; }
    public IReadOnlyDictionary<uint, (int Quantity, bool HqOnly)> Inputs { get; }
    public IReadOnlyDictionary<uint, ItemSummary> CheapestById { get; }

    public bool Covers(ShoppingEntry entry)
        => Inputs.TryGetValue(entry.ItemId, out var input) && input.Quantity == entry.Quantity && input.HqOnly == entry.HqOnly;
}
