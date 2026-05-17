using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;

namespace AuxMarketboard.Services;

public sealed class ItemResolver
{
    private readonly IDataManager dataManager;
    private Dictionary<uint, string>? namesById;
    private List<(uint Id, string Name)>? indexedItems;
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

        var trimmed = filter.Trim();
        var exact = indexedItems.Where(x => x.Name.Contains(trimmed, StringComparison.OrdinalIgnoreCase)).Take(maxResults).ToList();
        return exact;
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

        if (uint.TryParse(value, out var parsedId) && namesById is not null && namesById.TryGetValue(parsedId, out var parsedName))
        {
            itemId = parsedId;
            name = parsedName;
            return true;
        }

        if (indexedItems is null)
        {
            return false;
        }

        var match = indexedItems.FirstOrDefault(x => x.Name.Equals(value.Trim(), StringComparison.OrdinalIgnoreCase));
        if (match.Id != 0)
        {
            itemId = match.Id;
            name = match.Name;
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
        if (indexedItems is not null && namesById is not null)
        {
            return;
        }

        var sheet = dataManager.GetExcelSheet<Item>();
        var recipeSheet = dataManager.GetExcelSheet<Recipe>();
        namesById = new Dictionary<uint, string>();
        indexedItems = new List<(uint Id, string Name)>();
        recipeToResultItem = new Dictionary<uint, (uint ItemId, string ItemName, int Yield)>();

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
        }

        indexedItems = indexedItems.OrderBy(x => x.Name).ToList();

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
}
