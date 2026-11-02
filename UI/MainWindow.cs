using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using AuxMarketboard.Services;
using AuxUi;

namespace AuxMarketboard.UI;

public sealed class MainWindow : Window, IDisposable
{
    private static readonly string[] TabLabels = { "Shopping list", "Buy plan" };
    private static readonly TitleBarAction[] TitleActions = { new("Refresh", true) };

    private readonly MarketService service;
    private readonly ShoppingListTab shoppingListTab;
    private readonly BuyPlanTab buyPlanTab;

    private IDisposable? themeScope;
    private int selectedTab;

    internal MainWindow(MarketService service, ItemResolver resolver, ListImporter importer)
        : base("AuxMarketboard", ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
    {
        this.service = service;
        shoppingListTab = new ShoppingListTab(service, resolver, importer);
        buyPlanTab = new BuyPlanTab(service, resolver);
    }

    public override void PreDraw()
    {
        themeScope?.Dispose();
        themeScope = AuxTheme.Push();
        var scale = ImGuiHelpers.GlobalScale;
        ImGui.SetNextWindowSize(new Vector2(900f, 620f) * scale, ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSizeConstraints(new Vector2(700f, 460f) * scale, new Vector2(float.MaxValue, float.MaxValue));
    }

    public override void PostDraw()
    {
        themeScope?.Dispose();
        themeScope = null;
    }

    public override void Draw()
    {
        service.Tick();

        var bar = AuxWidgets.TitleBar("AuxMarketboard", TitleActions, AuxWidgets.CloseButton);
        if (bar.Action == 0)
        {
            service.Refresh();
        }

        if (bar.Button == 0)
        {
            IsOpen = false;
        }

        StatusStrip.Draw(service);
        ImGui.Spacing();
        selectedTab = AuxWidgets.Tabs("##mainTabs", TabLabels, selectedTab);
        ImGui.Spacing();

        if (ImGui.BeginChild("##tabContent", ImGui.GetContentRegionAvail(), false, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse))
        {
            if (selectedTab == 0)
            {
                shoppingListTab.Draw();
            }
            else
            {
                buyPlanTab.Draw();
            }
        }

        ImGui.EndChild();
    }

    public void Dispose()
    {
        themeScope = null;
    }
}
