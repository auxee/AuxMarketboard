using AuxMarketboard.Services;

namespace AuxMarketboard.UI;

internal sealed class ShoppingListTab
{
    private readonly AddItemPanel addItemPanel;
    private readonly ImportPanel importPanel;
    private readonly ItemsTable itemsTable;

    public ShoppingListTab(MarketService service, ItemResolver resolver, ListImporter importer)
    {
        addItemPanel = new AddItemPanel(service, resolver);
        importPanel = new ImportPanel(service, importer);
        itemsTable = new ItemsTable(service, resolver);
    }

    public void Draw()
    {
        addItemPanel.Draw();
        importPanel.Draw();
        itemsTable.Draw();
    }
}
