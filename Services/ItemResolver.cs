using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;

namespace AuxMarketboard.Services;

public sealed class ItemResolver
{
    private readonly IDataManager dataManager;
    private Dictionary<uint, string>? namesById;
    private List<(uint Id, string Name)>? indexedItems;
    private Dictionary<string, List<(uint Id, string Name)>>? normalizedItems;
    private Dictionary<uint, (uint ItemId, string ItemName, int Yield)>? recipeToResultItem;

    public ItemResolver(IDataManager dataManager)
    {
        this.dataManager = dataManager;
    }

    public IReadOnlyList<(uint Id, string Name)> GetSearchMatches(string filter, int maxResults = 100)
    {
        EnsureIndex();
        if (indexedItems is null)
        {
            return Array.Empty<(uint Id, string Name)>();
        }

        if (string.IsNullOrWhiteSpace(filter))
        {
            return indexedItems.Take(maxResults).ToList();
        }

        var trimmed = CleanupInput(filter);
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return indexedItems.Take(maxResults).ToList();
        }

        var seen = new HashSet<uint>();
        var results = new List<(uint Id, string Name)>();

        if (TryParseItemId(trimmed, out var parsedId) && namesById is not null && namesById.TryGetValue(parsedId, out var parsedName))
        {
            results.Add((parsedId, parsedName));
            seen.Add(parsedId);
        }

        foreach (var item in indexedItems.Where(x => x.Name.Equals(trimmed, StringComparison.OrdinalIgnoreCase)))
        {
            if (seen.Add(item.Id))
            {
                results.Add(item);
            }
        }

        foreach (var item in indexedItems.Where(x => x.Name.StartsWith(trimmed, StringComparison.OrdinalIgnoreCase)))
        {
            if (seen.Add(item.Id))
            {
                results.Add(item);
            }
        }

        var normalizedFilter = NormalizeLookupToken(trimmed);
        if (!string.IsNullOrWhiteSpace(normalizedFilter))
        {
            foreach (var item in indexedItems.Where(x => NormalizeLookupToken(x.Name).StartsWith(normalizedFilter, StringComparison.Ordinal)))
            {
                if (seen.Add(item.Id))
                {
                    results.Add(item);
                }
            }
        }

        foreach (var item in indexedItems.Where(x => x.Name.Contains(trimmed, StringComparison.OrdinalIgnoreCase)))
        {
            if (seen.Add(item.Id))
            {
                results.Add(item);
            }
        }

        if (!string.IsNullOrWhiteSpace(normalizedFilter))
        {
            foreach (var item in indexedItems.Where(x => NormalizeLookupToken(x.Name).Contains(normalizedFilter, StringComparison.Ordinal)))
            {
                if (seen.Add(item.Id))
                {
                    results.Add(item);
                }
            }
        }

        return results.Take(maxResults).ToList();
    }

    public bool TryGetName(uint itemId, out string name)
    {
        EnsureIndex();
        if (namesById is not null && namesById.TryGetValue(itemId, out var resolved))
        {
            name = resolved ?? string.Empty;
            return true;
        }

        name = string.Empty;
        return false;
    }

    public bool TryResolveByText(string value, out uint itemId, out string name)
    {
        EnsureIndex();
        itemId = 0;
        name = string.Empty;

        var normalizedInput = CleanupInput(value);
        if (string.IsNullOrWhiteSpace(normalizedInput))
        {
            return false;
        }

        if (TryParseItemId(normalizedInput, out var parsedId) && namesById is not null && namesById.TryGetValue(parsedId, out var parsedName))
        {
            itemId = parsedId;
            name = parsedName;
            return true;
        }

        if (indexedItems is null || normalizedItems is null)
        {
            return false;
        }

        var match = indexedItems.FirstOrDefault(x => x.Name.Equals(normalizedInput, StringComparison.OrdinalIgnoreCase));
        if (match.Id != 0)
        {
            itemId = match.Id;
            name = match.Name;
            return true;
        }

        var key = NormalizeLookupToken(normalizedInput);
        if (!string.IsNullOrWhiteSpace(key) && normalizedItems.TryGetValue(key, out var candidates) && candidates.Count > 0)
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
        EnsureIndex();
        itemId = 0;
        itemName = string.Empty;
        amountPerCraft = 1;

        if (recipeToResultItem is not null && recipeToResultItem.TryGetValue(recipeId, out var mapped))
        {
            itemId = mapped.ItemId;
            itemName = mapped.ItemName;
            amountPerCraft = Math.Max(1, mapped.Yield);
            return true;
        }

        return false;
    }

    private void EnsureIndex()
    {
        if (indexedItems is not null && namesById is not null && normalizedItems is not null)
        {
            return;
        }

        var sheet = dataManager.GetExcelSheet<Item>();
        var recipeSheet = dataManager.GetExcelSheet<Recipe>();
        namesById = new Dictionary<uint, string>();
        indexedItems = new List<(uint Id, string Name)>();
        normalizedItems = new Dictionary<string, List<(uint Id, string Name)>>(StringComparer.Ordinal);
        recipeToResultItem = new Dictionary<uint, (uint ItemId, string ItemName, int Yield)>();

        if (sheet is not null)
        {
            foreach (var row in sheet)
            {
                if (row.RowId == 0)
                {
                    continue;
                }

                var name = row.Name.ToString().Trim();
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                namesById[row.RowId] = name;
                indexedItems.Add((row.RowId, name));

                var key = NormalizeLookupToken(name);
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                if (!normalizedItems.TryGetValue(key, out var list))
                {
                    list = new List<(uint Id, string Name)>();
                    normalizedItems[key] = list;
                }

                list.Add((row.RowId, name));
            }
        }

        indexedItems = indexedItems.OrderBy(x => x.Name).ToList();

        if (recipeSheet is null)
        {
            return;
        }

        foreach (var recipe in recipeSheet)
        {
            if (recipe.RowId == 0 || recipe.ItemResult.RowId == 0)
            {
                continue;
            }

            var resultItemId = recipe.ItemResult.RowId;
            if (!namesById.TryGetValue(resultItemId, out var resultName))
            {
                continue;
            }

            recipeToResultItem[recipe.RowId] = (resultItemId, resultName, recipe.AmountResult);
        }
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

        var matches = System.Text.RegularExpressions.Regex.Matches(
            value,
            @"(?<!\d)(?:item\s*id|id|#)\s*[:=]?\s*(\d+)(?!\d)|\((\d+)\)");
        foreach (System.Text.RegularExpressions.Match match in matches)
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
}
