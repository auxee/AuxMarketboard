using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using AuxMarketboard.Services;
using AuxUi;

namespace AuxMarketboard.UI;

internal sealed class BuyPlanTab
{
    private static readonly string[] WorldLimitLabels = { "Any", "1", "2", "3", "4", "5" };
    private static readonly string[] PlanHeaders = Array.Empty<string>();

    private readonly MarketService service;
    private readonly ItemResolver resolver;
    private readonly HashSet<string> collapsedWorlds = new(StringComparer.Ordinal);

    public BuyPlanTab(MarketService service, ItemResolver resolver)
    {
        this.service = service;
        this.resolver = resolver;
    }

    public void Draw()
    {
        DrawControls();

        var snapshot = service.Snapshot;
        var plan = snapshot.Plan;
        DrawSummary(plan);

        ImGui.Spacing();
        if (ImGui.BeginChild("##planScroll", ImGui.GetContentRegionAvail(), false, ImGuiWindowFlags.None))
        {
            if (service.Items.Count == 0)
            {
                DrawMessage("Nothing to buy yet", "Add items on the Shopping list tab to see where to buy them.");
            }
            else if (!service.Scope.IsKnown)
            {
                DrawMessage("Log in to detect your world", "Prices are fetched for the region of your home world.");
            }
            else if (plan.Worlds.Count == 0 && plan.Items.Count == 0)
            {
                DrawMessage(service.State.Phase == RefreshPhase.Failed ? "Could not load prices" : "Fetching prices…", "The plan appears here once prices have loaded.");
            }
            else
            {
                DrawWorlds(plan);
                DrawShortItems(plan);
            }
        }

        ImGui.EndChild();
    }

    private void DrawControls()
    {
        var start = ImGui.GetCursorScreenPos();
        var height = ImGui.GetFrameHeight();
        var gap = 10f * UiFormat.Scale;

        UiFormat.AddText(new Vector2(start.X, UiFormat.CenteredTextY(start.Y, height)), AuxTheme.MutedFg, "Max worlds");
        var labelWidth = ImGui.CalcTextSize("Max worlds").X;
        ImGui.SetCursorScreenPos(new Vector2(start.X + labelWidth + gap, start.Y));
        var selected = AuxWidgets.Tabs("##maxWorlds", WorldLimitLabels, service.MaxWorlds);
        if (selected != service.MaxWorlds)
        {
            service.SetMaxWorlds(selected);
        }

        ImGui.SetCursorScreenPos(new Vector2(start.X, start.Y + height + AuxTheme.ItemSpacing.Y));
    }

    private void DrawSummary(BuyPlan plan)
    {
        if (plan.Items.Count == 0)
        {
            return;
        }

        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(AuxTheme.MutedFg, "Total");
        ImGui.SameLine();
        using (AuxFonts.PushBold())
        {
            ImGui.TextUnformatted(UiFormat.Gil(plan.Total));
        }

        var worldCount = plan.Worlds.Count;
        ImGui.SameLine();
        ImGui.TextColored(AuxTheme.MutedFg, $"across {worldCount} {(worldCount == 1 ? "world" : "worlds")}");

        if (service.MaxWorlds > 0 && plan.DeltaVsCheapest > 0)
        {
            ImGui.SameLine();
            ImGui.TextColored(AuxTheme.MutedFg, $"(+{UiFormat.Number(plan.DeltaVsCheapest)} vs cheapest)");
        }
    }

    private static void DrawMessage(string title, string detail)
    {
        ImGui.Dummy(new Vector2(0f, 18f * UiFormat.Scale));
        UiFormat.CenteredText(title, AuxTheme.ContentFg);
        UiFormat.CenteredText(detail, AuxTheme.MutedFg);
    }

    private void DrawWorlds(BuyPlan plan)
    {
        var homeDataCenter = service.Scope.DataCenter;
        var homeWorld = service.HomeWorldName;
        var itemSummaries = plan.Items.ToDictionary(x => x.ItemId);
        var ordered = plan.Worlds
            .OrderBy(x => string.Equals(x.DataCenter, homeDataCenter, StringComparison.Ordinal) ? 0 : 1)
            .ThenBy(x => x.DataCenter, StringComparer.Ordinal)
            .ThenByDescending(x => x.Subtotal)
            .ToList();

        foreach (var world in ordered)
        {
            ImGui.PushID(world.World);
            var collapsed = collapsedWorlds.Contains(world.World);
            if (DrawWorldHeader(world, world.World == homeWorld, collapsed))
            {
                if (!collapsedWorlds.Remove(world.World))
                {
                    collapsedWorlds.Add(world.World);
                }
            }

            if (!collapsed)
            {
                DrawPurchases(world, itemSummaries);
                ImGui.Spacing();
            }

            ImGui.PopID();
        }
    }

    private bool DrawWorldHeader(PlanWorld world, bool isHome, bool collapsed)
    {
        var scale = UiFormat.Scale;
        var height = ImGui.GetFrameHeight();
        var width = ImGui.GetContentRegionAvail().X;
        var min = ImGui.GetCursorScreenPos();
        var pressed = AuxWidgets.RoundedSelectable("##header", false, new Vector2(width, height));
        var after = ImGui.GetCursorScreenPos();
        var inset = 10f * scale;
        var gap = 8f * scale;
        var textY = UiFormat.CenteredTextY(min.Y, height);
        var x = min.X + inset;

        var caret = ((char)(collapsed ? FontAwesomeIcon.CaretRight : FontAwesomeIcon.CaretDown)).ToString();
        using (ImRaii.PushFont(UiBuilder.IconFont))
        {
            UiFormat.AddText(new Vector2(x, UiFormat.CenteredTextY(min.Y, height)), AuxTheme.MutedFg, caret);
            x += ImGui.CalcTextSize(caret).X + gap;
        }

        using (AuxFonts.PushBold())
        {
            UiFormat.AddText(new Vector2(x, textY), AuxTheme.ContentFg, world.World);
            x += ImGui.CalcTextSize(world.World).X + gap;
        }

        if (world.DataCenter.Length > 0)
        {
            UiFormat.AddText(new Vector2(x, textY), AuxTheme.MutedFg, world.DataCenter);
            x += ImGui.CalcTextSize(world.DataCenter).X + gap;
        }

        if (isHome)
        {
            ImGui.SetCursorScreenPos(new Vector2(x, min.Y + (height - AuxWidgets.ChipHeight()) / 2f));
            AuxWidgets.StatusChip("Home", true);
        }

        var right = min.X + width - inset;
        var count = world.Purchases.Count;
        var countText = $"{count} {(count == 1 ? "item" : "items")}";
        right -= ImGui.CalcTextSize(countText).X;
        UiFormat.AddText(new Vector2(right, textY), AuxTheme.MutedFg, countText);

        var subtotal = UiFormat.Gil(world.Subtotal);
        using (AuxFonts.PushBold())
        {
            right -= ImGui.CalcTextSize(subtotal).X + gap * 2f;
            UiFormat.AddText(new Vector2(right, textY), AuxTheme.ContentFg, subtotal);
        }

        ImGui.SetCursorScreenPos(after);
        return pressed;
    }

    private void DrawPurchases(PlanWorld world, Dictionary<uint, ItemSummary> itemSummaries)
    {
        var scale = UiFormat.Scale;
        var purchases = world.Purchases.OrderByDescending(x => x.Cost).ToList();

        if (!AuxWidgets.BeginTable("##purchases", 4, padOuter: true))
        {
            return;
        }

        ImGui.TableSetupColumn("Item", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Stacks", ImGuiTableColumnFlags.WidthFixed, 190f * scale);
        ImGui.TableSetupColumn("Units", ImGuiTableColumnFlags.WidthFixed, 150f * scale);
        ImGui.TableSetupColumn("Cost", ImGuiTableColumnFlags.WidthFixed, 130f * scale);
        AuxWidgets.TableHeaderRow("purchases", PlanHeaders, TableAlign.Left, TableAlign.Left, TableAlign.Left, TableAlign.Right);

        foreach (var purchase in purchases)
        {
            ImGui.TableNextRow(ImGuiTableRowFlags.None, ImGui.GetFrameHeight());

            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            UiFormat.MarkedText(resolver.TryGetName(purchase.ItemId, out var name) ? name : $"Item {purchase.ItemId}");

            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted(StackText(purchase));

            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            DrawUnits(purchase, itemSummaries);

            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            UiFormat.RightAlignedText(UiFormat.Gil(purchase.Cost), AuxTheme.ContentFg);
        }

        ImGui.EndTable();
    }

    private static string StackText(PlanPurchase purchase)
    {
        var groups = purchase.Stacks
            .GroupBy(x => x.Quantity)
            .OrderByDescending(x => x.Key)
            .Select(x => $"{x.Count()}×{x.Key}");
        return string.Join(", ", groups);
    }

    private static void DrawUnits(PlanPurchase purchase, Dictionary<uint, ItemSummary> itemSummaries)
    {
        if (!itemSummaries.TryGetValue(purchase.ItemId, out var summary))
        {
            ImGui.TextUnformatted(UiFormat.Number(purchase.Units));
            return;
        }

        ImGui.TextUnformatted($"{UiFormat.Number(purchase.Units)} / {UiFormat.Number(summary.Needed)}");
        if (summary.Overbuy > 0 && purchase.Units == summary.Bought)
        {
            ImGui.SameLine();
            ImGui.TextColored(AuxTheme.MutedFg, $"+{UiFormat.Number(summary.Overbuy)}");
        }
    }

    private void DrawShortItems(BuyPlan plan)
    {
        var shortItems = plan.Items.Where(x => x.Short > 0).ToList();
        if (shortItems.Count == 0)
        {
            return;
        }

        ImGui.Spacing();
        AuxWidgets.SectionLabel("Not enough on market");
        foreach (var item in shortItems)
        {
            var name = resolver.TryGetName(item.ItemId, out var resolved) ? resolved : $"Item {item.ItemId}";
            ImGui.TextUnformatted(name);
            ImGui.SameLine();
            ImGui.TextColored(AuxTheme.MutedFg, $"short by {UiFormat.Number(item.Short)} of {UiFormat.Number(item.Needed)}");
        }
    }
}
