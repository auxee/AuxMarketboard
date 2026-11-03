namespace AuxMarketboard.Services;

public sealed record ItemMarket(uint ItemId, IReadOnlyList<MarketListing> Listings, long LastUploadTime, bool Complete)
{
    public static ItemMarket None(uint itemId) => new(itemId, Array.Empty<MarketListing>(), 0, true);
}

public sealed record DataCenterInfo(string Name, string Region, IReadOnlyList<uint> WorldIds);

public sealed record WorldInfo(uint Id, string Name);
