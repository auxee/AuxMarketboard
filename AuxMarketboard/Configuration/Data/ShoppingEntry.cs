namespace AuxMarketboard;

public sealed class ShoppingEntry
{
    public uint ItemId { get; set; }
    public int Quantity { get; set; } = 1;
    public bool HqOnly { get; set; }
}
