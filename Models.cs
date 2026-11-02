namespace AuxMarketboard;

public enum ScopeMode
{
    World,
    DataCenter,
    Region,
}

public sealed class ShoppingEntry
{
    public uint ItemId { get; set; }
    public int Quantity { get; set; } = 1;
    public bool HqOnly { get; set; }
}

public sealed record ImportedItem(uint ItemId, int Quantity);
