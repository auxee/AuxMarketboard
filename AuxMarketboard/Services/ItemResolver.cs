using System.Text.RegularExpressions;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;

namespace AuxMarketboard.Services;

public sealed class ItemResolver
{
    private readonly IDataManager dataManager;
    private readonly object indexLock = new();
    private Index? index;

    public ItemResolver(IDataManager dataManager)
    {
        this.dataManager = dataManager;
    }

    public void Warm()
    {
        Task.Run(() =>
        {
            try
            {
                GetIndex();
            }
            catch (Exception)
            {
            }
        });
    }

    public IReadOnlyList<(uint Id, string Name)> GetSearchMatches(string filter, int maxResults = 100)
    {
        var current = GetIndex();
        var trimmed = CleanupInput(filter);
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return current.Items.Take(maxResults).Select(x => (x.Id, x.Name)).ToList();
        }

        var seen = new HashSet<uint>();
        var results = new List<(uint Id, string Name)>();

        void Collect(Func<IndexedItem, bool> predicate)
        {
            foreach (var item in current.Items)
            {
                if (results.Count >= maxResults)
                {
                    return;
                }

                if (predicate(item) && seen.Add(item.Id))
                {
                    results.Add((item.Id, item.Name));
                }
            }
        }

        if (TryParseItemId(trimmed, out var parsedId) && current.NamesById.TryGetValue(parsedId, out var parsedName))
        {
            results.Add((parsedId, parsedName));
            seen.Add(parsedId);
        }

        var normalizedFilter = NormalizeLookupToken(trimmed);
        Collect(x => x.Name.Equals(trimmed, StringComparison.OrdinalIgnoreCase));
        Collect(x => x.Name.StartsWith(trimmed, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(normalizedFilter))
        {
            Collect(x => x.Key.StartsWith(normalizedFilter, StringComparison.Ordinal));
        }

        Collect(x => x.Name.Contains(trimmed, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(normalizedFilter))
        {
            Collect(x => x.Key.Contains(normalizedFilter, StringComparison.Ordinal));
        }

        return results.Take(maxResults).ToList();
    }

    public bool TryGetName(uint itemId, out string name)
    {
        if (GetIndex().NamesById.TryGetValue(itemId, out var resolved))
        {
            name = resolved;
            return true;
        }

        name = string.Empty;
        return false;
    }

    public bool CanBeHq(uint itemId) => GetIndex().HqCapable.Contains(itemId);

    public bool TryResolveByText(string value, out uint itemId, out string name)
    {
        var current = GetIndex();
        itemId = 0;
        name = string.Empty;

        var normalizedInput = CleanupInput(value);
        if (string.IsNullOrWhiteSpace(normalizedInput))
        {
            return false;
        }

        if (TryParseItemId(normalizedInput, out var parsedId) && current.NamesById.TryGetValue(parsedId, out var parsedName))
        {
            itemId = parsedId;
            name = parsedName;
            return true;
        }

        var match = current.Items.FirstOrDefault(x => x.Name.Equals(normalizedInput, StringComparison.OrdinalIgnoreCase));
        if (match.Id != 0)
        {
            itemId = match.Id;
            name = match.Name;
            return true;
        }

        var key = NormalizeLookupToken(normalizedInput);
        if (!string.IsNullOrWhiteSpace(key) && current.ByKey.TryGetValue(key, out var candidates) && candidates.Count > 0)
        {
            var selected = candidates.OrderBy(x => x.Name.Length).ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase).First();
            itemId = selected.Id;
            name = selected.Name;
            return true;
        }

        return false;
    }

    public bool TryResolveRecipeToItem(uint recipeId, out uint itemId, out string itemName, out int amountPerCraft)
    {
        itemId = 0;
        itemName = string.Empty;
        amountPerCraft = 1;

        if (GetIndex().RecipeResults.TryGetValue(recipeId, out var mapped))
        {
            itemId = mapped.ItemId;
            itemName = mapped.ItemName;
            amountPerCraft = Math.Max(1, mapped.Yield);
            return true;
        }

        return false;
    }

    private Index GetIndex()
    {
        var existing = index;
        if (existing is not null)
        {
            return existing;
        }

        lock (indexLock)
        {
            index ??= BuildIndex();
            return index;
        }
    }

    private Index BuildIndex()
    {
        var sheet = dataManager.GetExcelSheet<Item>();
        var recipeSheet = dataManager.GetExcelSheet<Recipe>();
        var namesById = new Dictionary<uint, string>();
        var items = new List<IndexedItem>();
        var byKey = new Dictionary<string, List<IndexedItem>>(StringComparer.Ordinal);
        var hqCapable = new HashSet<uint>();
        var recipeResults = new Dictionary<uint, (uint ItemId, string ItemName, int Yield)>();

        if (sheet is not null)
        {
            foreach (var row in sheet)
            {
                if (row.RowId == 0 || row.ItemSearchCategory.RowId == 0)
                {
                    continue;
                }

                var name = row.Name.ToString().Trim();
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                var key = NormalizeLookupToken(name);
                var indexed = new IndexedItem(row.RowId, name, key);
                namesById[row.RowId] = name;
                items.Add(indexed);
                if (row.CanBeHq)
                {
                    hqCapable.Add(row.RowId);
                }

                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                if (!byKey.TryGetValue(key, out var list))
                {
                    list = new List<IndexedItem>();
                    byKey[key] = list;
                }

                list.Add(indexed);
            }
        }

        items = items.OrderBy(x => x.Name, StringComparer.Ordinal).ToList();

        if (recipeSheet is not null)
        {
            foreach (var recipe in recipeSheet)
            {
                if (recipe.RowId == 0 || recipe.ItemResult.RowId == 0)
                {
                    continue;
                }

                var resultItemId = recipe.ItemResult.RowId;
                if (namesById.TryGetValue(resultItemId, out var resultName))
                {
                    recipeResults[recipe.RowId] = (resultItemId, resultName, recipe.AmountResult);
                }
            }
        }

        return new Index(namesById, items, byKey, hqCapable, recipeResults);
    }

    private static string CleanupInput(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var cleaned = value.Trim();
        cleaned = cleaned.Trim('"', '\'', '`', '[', ']');
        return string.Join(' ', cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static bool TryParseItemId(string value, out uint itemId)
    {
        itemId = 0;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (uint.TryParse(value.Trim(), out itemId))
        {
            return itemId > 0;
        }

        var matches = Regex.Matches(
            value,
            @"(?<!\d)(?:item\s*id|id|#)\s*[:=]?\s*(\d+)(?!\d)|\((\d+)\)");
        foreach (Match match in matches)
        {
            var token = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
            if (uint.TryParse(token, out itemId) && itemId > 0)
            {
                return true;
            }
        }

        return false;
    }

    private static string NormalizeLookupToken(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var cleaned = CleanupInput(value);
        cleaned = cleaned.ToLowerInvariant()
            .Replace("(hq)", string.Empty)
            .Replace("[hq]", string.Empty)
            .Replace("hq", string.Empty)
            .Replace("nq", string.Empty)
            .Replace("’", "'")
            .Replace("'", string.Empty)
            .Replace("-", string.Empty)
            .Replace("_", string.Empty)
            .Replace(",", string.Empty)
            .Replace(".", string.Empty);

        return string.Concat(cleaned.Where(char.IsLetterOrDigit));
    }

    private readonly record struct IndexedItem(uint Id, string Name, string Key);

    private sealed record Index(
        Dictionary<uint, string> NamesById,
        List<IndexedItem> Items,
        Dictionary<string, List<IndexedItem>> ByKey,
        HashSet<uint> HqCapable,
        Dictionary<uint, (uint ItemId, string ItemName, int Yield)> RecipeResults);
}
