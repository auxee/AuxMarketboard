namespace AuxMarketboard.Services;

public readonly record struct MarketListing(string World, int UnitPrice, int Quantity, long Cost, bool Hq);

public sealed record PricingRequest(uint ItemId, int Quantity, bool HqOnly, IReadOnlyList<MarketListing> Listings);

public sealed record ItemFill(IReadOnlyList<MarketListing> Chosen, int Needed, int UnitsBought, int Overbuy, int Short, long Cost)
{
    public double EffectiveUnitPrice
    {
        get
        {
            var used = UnitsBought - Overbuy;
            return used > 0 ? (double)Cost / used : 0d;
        }
    }
}

public sealed record ItemSummary(uint ItemId, int Needed, int Bought, int Overbuy, int Short, long Cost);

public sealed record PlanPurchase(uint ItemId, IReadOnlyList<MarketListing> Stacks, int Units, long Cost);

public sealed record PlanWorld(string World, string DataCenter, IReadOnlyList<PlanPurchase> Purchases, long Subtotal);

public sealed record BuyPlan(
    IReadOnlyList<PlanWorld> Worlds,
    IReadOnlyList<ItemSummary> Items,
    IReadOnlyList<ItemSummary> CheapestItems,
    long Total,
    long CheapestTotal)
{
    public long DeltaVsCheapest => Total - CheapestTotal;

    public static readonly BuyPlan Empty = new(
        Array.Empty<PlanWorld>(),
        Array.Empty<ItemSummary>(),
        Array.Empty<ItemSummary>(),
        0,
        0);
}

public static class PricingEngine
{
    private const long ShortfallPenaltyPerUnit = 1_000_000_000L;
    private const long MaxDpCells = 60_000_000L;

    public static IReadOnlyList<MarketListing> Eligible(IReadOnlyList<MarketListing> listings, bool hqOnly)
    {
        if (!hqOnly)
        {
            return listings;
        }

        var result = new List<MarketListing>(listings.Count);
        foreach (var listing in listings)
        {
            if (listing.Hq)
            {
                result.Add(listing);
            }
        }

        return result;
    }

    public static ItemFill FillExact(IReadOnlyList<MarketListing> eligible, int needed)
    {
        if (needed <= 0)
        {
            return new ItemFill(Array.Empty<MarketListing>(), 0, 0, 0, 0, 0);
        }

        long totalQuantity = 0;
        var maxStack = 0;
        foreach (var listing in eligible)
        {
            totalQuantity += listing.Quantity;
            maxStack = Math.Max(maxStack, listing.Quantity);
        }

        if (totalQuantity <= needed)
        {
            return BuildFill(eligible.ToList(), needed);
        }

        var count = eligible.Count;
        var cap = needed + maxStack;
        if ((long)count * cap > MaxDpCells)
        {
            return FillGreedy(eligible, needed);
        }

        var cost = new long[cap];
        var stacks = new int[cap];
        Array.Fill(cost, long.MaxValue);
        cost[0] = 0;
        var wordsPerRow = (cap + 63) / 64;
        var taken = new ulong[(long)count * wordsPerRow];

        for (var i = 0; i < count; i++)
        {
            var quantity = eligible[i].Quantity;
            var listingCost = eligible[i].Cost;
            if (quantity <= 0)
            {
                continue;
            }

            var rowOffset = (long)i * wordsPerRow;
            for (var s = cap - 1; s >= quantity; s--)
            {
                var previous = cost[s - quantity];
                if (previous == long.MaxValue)
                {
                    continue;
                }

                var candidate = previous + listingCost;
                var candidateStacks = stacks[s - quantity] + 1;
                if (candidate < cost[s] || (candidate == cost[s] && candidateStacks < stacks[s]))
                {
                    cost[s] = candidate;
                    stacks[s] = candidateStacks;
                    taken[rowOffset + (s >> 6)] |= 1UL << (s & 63);
                }
            }
        }

        var bestSum = -1;
        for (var s = needed; s < cap; s++)
        {
            if (cost[s] == long.MaxValue)
            {
                continue;
            }

            if (bestSum < 0 || cost[s] < cost[bestSum])
            {
                bestSum = s;
            }
        }

        if (bestSum < 0)
        {
            return BuildFill(eligible.ToList(), needed);
        }

        var chosen = new List<MarketListing>();
        var remaining = bestSum;
        for (var i = count - 1; i >= 0 && remaining > 0; i--)
        {
            var rowOffset = (long)i * wordsPerRow;
            if ((taken[rowOffset + (remaining >> 6)] & (1UL << (remaining & 63))) != 0)
            {
                chosen.Add(eligible[i]);
                remaining -= eligible[i].Quantity;
            }
        }

        chosen.Reverse();
        return BuildFill(chosen, needed);
    }

    public static ItemFill FillGreedy(IReadOnlyList<MarketListing> eligible, int needed)
    {
        if (needed <= 0)
        {
            return new ItemFill(Array.Empty<MarketListing>(), 0, 0, 0, 0, 0);
        }

        var ordered = eligible.OrderBy(x => x.UnitPrice).ThenBy(x => x.Cost).ToList();
        var chosen = new List<MarketListing>();
        long units = 0;
        foreach (var listing in ordered)
        {
            if (units >= needed)
            {
                break;
            }

            chosen.Add(listing);
            units += listing.Quantity;
        }

        return BuildFill(chosen, needed);
    }

    public static BuyPlan BuildPlan(IReadOnlyList<PricingRequest> requests, int maxWorlds, Func<string, string>? dataCenterOf = null)
    {
        var items = new List<ItemData>(requests.Count);
        var worldIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        var worldNames = new List<string>();

        foreach (var request in requests)
        {
            if (request.Quantity <= 0)
            {
                continue;
            }

            var eligible = Eligible(request.Listings, request.HqOnly);
            var sorted = eligible.OrderBy(x => x.UnitPrice).ThenBy(x => x.Cost).ToArray();
            var indexes = new int[sorted.Length];
            for (var i = 0; i < sorted.Length; i++)
            {
                var world = sorted[i].World ?? string.Empty;
                if (!worldIndex.TryGetValue(world, out var index))
                {
                    index = worldNames.Count;
                    worldIndex[world] = index;
                    worldNames.Add(world);
                }

                indexes[i] = index;
            }

            items.Add(new ItemData(request, eligible, sorted, indexes));
        }

        var cheapestFills = items.Select(x => FillExact(x.Eligible, x.Request.Quantity)).ToList();
        var cheapestSummaries = items.Select((x, i) => Summarise(x.Request, cheapestFills[i])).ToList();
        var cheapestTotal = cheapestFills.Sum(x => x.Cost);

        List<ItemFill> fills;
        if (maxWorlds <= 0 || worldNames.Count <= maxWorlds)
        {
            fills = cheapestFills;
        }
        else
        {
            var mask = SelectWorlds(items, worldNames.Count, maxWorlds);
            fills = items
                .Select(x => FillExact(x.Eligible.Where(l => mask[worldIndex[l.World ?? string.Empty]]).ToList(), x.Request.Quantity))
                .ToList();
        }

        var summaries = items.Select((x, i) => Summarise(x.Request, fills[i])).ToList();
        var worlds = GroupByWorld(items, fills, dataCenterOf);
        var total = fills.Sum(x => x.Cost);
        return new BuyPlan(worlds, summaries, cheapestSummaries, total, cheapestTotal);
    }

    private static bool[] SelectWorlds(List<ItemData> items, int worldCount, int maxWorlds)
    {
        var mask = new bool[worldCount];
        var currentTotal = RestrictedTotal(items, mask);
        for (var step = 0; step < maxWorlds; step++)
        {
            var bestWorld = -1;
            var bestTotal = currentTotal;
            for (var world = 0; world < worldCount; world++)
            {
                if (mask[world])
                {
                    continue;
                }

                mask[world] = true;
                var candidate = RestrictedTotal(items, mask);
                mask[world] = false;
                if (candidate < bestTotal)
                {
                    bestTotal = candidate;
                    bestWorld = world;
                }
            }

            if (bestWorld < 0)
            {
                break;
            }

            mask[bestWorld] = true;
            currentTotal = bestTotal;
        }

        var improved = true;
        while (improved)
        {
            improved = false;
            for (var chosen = 0; chosen < worldCount && !improved; chosen++)
            {
                if (!mask[chosen])
                {
                    continue;
                }

                mask[chosen] = false;
                for (var replacement = 0; replacement < worldCount; replacement++)
                {
                    if (replacement == chosen || mask[replacement])
                    {
                        continue;
                    }

                    mask[replacement] = true;
                    var candidate = RestrictedTotal(items, mask);
                    if (candidate < currentTotal)
                    {
                        currentTotal = candidate;
                        improved = true;
                        break;
                    }

                    mask[replacement] = false;
                }

                if (!improved)
                {
                    mask[chosen] = true;
                }
            }
        }

        return mask;
    }

    private static long RestrictedTotal(List<ItemData> items, bool[] mask)
    {
        long total = 0;
        foreach (var item in items)
        {
            var needed = item.Request.Quantity;
            long cost = 0;
            long units = 0;
            for (var i = 0; i < item.Sorted.Length && units < needed; i++)
            {
                if (!mask[item.WorldIndexes[i]])
                {
                    continue;
                }

                cost += item.Sorted[i].Cost;
                units += item.Sorted[i].Quantity;
            }

            var missing = Math.Max(0L, needed - units);
            total += cost + missing * ShortfallPenaltyPerUnit;
        }

        return total;
    }

    private static List<PlanWorld> GroupByWorld(List<ItemData> items, List<ItemFill> fills, Func<string, string>? dataCenterOf)
    {
        var byWorld = new Dictionary<string, List<PlanPurchase>>(StringComparer.Ordinal);
        for (var i = 0; i < items.Count; i++)
        {
            foreach (var group in fills[i].Chosen.GroupBy(x => x.World ?? string.Empty))
            {
                var stacks = group.OrderByDescending(x => x.Quantity).ToList();
                var purchase = new PlanPurchase(items[i].Request.ItemId, stacks, stacks.Sum(x => x.Quantity), stacks.Sum(x => x.Cost));
                if (!byWorld.TryGetValue(group.Key, out var list))
                {
                    list = new List<PlanPurchase>();
                    byWorld[group.Key] = list;
                }

                list.Add(purchase);
            }
        }

        return byWorld
            .Select(pair => new PlanWorld(pair.Key, dataCenterOf?.Invoke(pair.Key) ?? string.Empty, pair.Value, pair.Value.Sum(x => x.Cost)))
            .OrderByDescending(x => x.Subtotal)
            .ToList();
    }

    private static ItemSummary Summarise(PricingRequest request, ItemFill fill)
        => new(request.ItemId, request.Quantity, fill.UnitsBought, fill.Overbuy, fill.Short, fill.Cost);

    private static ItemFill BuildFill(List<MarketListing> chosen, int needed)
    {
        long units = 0;
        long cost = 0;
        foreach (var listing in chosen)
        {
            units += listing.Quantity;
            cost += listing.Cost;
        }

        var overbuy = (int)Math.Max(0L, units - needed);
        var missing = (int)Math.Max(0L, needed - units);
        return new ItemFill(chosen, needed, (int)units, overbuy, missing, cost);
    }

    private sealed record ItemData(PricingRequest Request, IReadOnlyList<MarketListing> Eligible, MarketListing[] Sorted, int[] WorldIndexes);
}
