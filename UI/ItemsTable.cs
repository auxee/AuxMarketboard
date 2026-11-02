using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using AuxMarketboard.Services;
using AuxUi;

namespace AuxMarketboard.UI;

internal sealed class ItemsTable
{
    private static readonly string[] NoHints = Array.Empty<string>();

    private readonly MarketService service;
    private readonly ItemResolver resolver;

    public ItemsTable(MarketService service, ItemResolver resolver)
    {
        this.service = service;
        this.resolver = resolver;
    }

    public void Draw()
    {
        var items = service.Items;
        AuxWidgets.SectionLabel($"Items ({items.Count})");

        if (items.Count == 0)
        {
            DrawEmptyState();
            return;
        }

        var footerHeight = ImGui.GetFrameHeight() + AuxTheme.ItemSpacing.Y;
        var listHeight = Math.Max(90f * UiFormat.Scale, ImGui.GetContentRegionAvail().Y - footerHeight);
        if (ImGui.BeginChild("##itemsScroll", new Vector2(0f, listHeight), false, ImGuiWindowFlags.None))
        {
            DrawTable(items);
        }

        ImGui.EndChild();
        DrawFooter();
    }

    private void DrawEmptyState()
    {
        ImGui.Dummy(new Vector2(0f, 18f * UiFormat.Scale));
        UiFormat.CenteredText("Your list is empty", AuxTheme.ContentFg);
        UiFormat.CenteredText("Search for an item above, or import a Teamcraft list, Artisan export or item lines.", AuxTheme.MutedFg);
    }

    private void DrawTable(IReadOnlyList<ShoppingEntry> items)
    {
        var scale = UiFormat.Scale;
        var snapshot = service.Snapshot;
        var busy = service.State.Phase is RefreshPhase.Waiting or RefreshPhase.Fetching;
        uint removeId = 0;

        if (!AuxWidgets.BeginTable("##items", 6, padOuter: true))
        {
            return;
        }

        ImGui.TableSetupColumn("Item", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Qty", ImGuiTableColumnFlags.WidthFixed, 140f * scale);
        ImGui.TableSetupColumn("HQ", ImGuiTableColumnFlags.WidthFixed, 56f * scale);
        ImGui.TableSetupColumn("Best total", ImGuiTableColumnFlags.WidthFixed, 130f * scale);
        ImGui.TableSetupColumn("Status", ImGuiTableColumnFlags.WidthFixed, 96f * scale);
        ImGui.TableSetupColumn(" ", ImGuiTableColumnFlags.WidthFixed, 40f * scale);
        AuxWidgets.TableHeaderRow("items", NoHints, TableAlign.Left, TableAlign.Left, TableAlign.Center, TableAlign.Right, TableAlign.Left, TableAlign.Center);

        foreach (var entry in items)
        {
            ImGui.PushID((int)entry.ItemId);
            ImGui.TableNextRow(ImGuiTableRowFlags.None, ImGui.GetFrameHeight());

            var covered = snapshot.Covers(entry);
            var hasSummary = snapshot.CheapestById.TryGetValue(entry.ItemId, out var summary);

            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            UiFormat.MarkedText(resolver.TryGetName(entry.ItemId, out var name) ? name : $"Item {entry.ItemId}");

            ImGui.TableNextColumn();
            var quantity = entry.Quantity;
            ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
            if (AuxWidgets.InputInt("##quantity", ref quantity))
            {
                service.SetQuantity(entry, quantity);
            }

            ImGui.TableNextColumn();
            DrawHqCell(entry);

            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            if (hasSummary && summary!.Cost > 0)
            {
                UiFormat.RightAlignedText(UiFormat.Gil(summary.Cost), covered ? AuxTheme.ContentFg : AuxTheme.MutedFg);
            }
            else
            {
                UiFormat.RightAlignedText("–", AuxTheme.MutedFg);
            }

            ImGui.TableNextColumn();
            DrawStatus(entry, snapshot, busy, covered, hasSummary ? summary : null);

            ImGui.TableNextColumn();
            if (AuxWidgets.IconButton(FontAwesomeIcon.Trash, "remove", "Remove", true))
            {
                removeId = entry.ItemId;
            }

            ImGui.PopID();
        }

        ImGui.EndTable();

        if (removeId != 0)
        {
            service.Remove(removeId);
        }
    }

    private void DrawHqCell(ShoppingEntry entry)
    {
        var frame = ImGui.GetFrameHeight();
        var available = ImGui.GetContentRegionAvail().X;
        if (resolver.CanBeHq(entry.ItemId))
        {
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + Math.Max(0f, (available - frame) / 2f));
            var hq = entry.HqOnly;
            if (AuxWidgets.Checkbox("##hq", ref hq))
            {
                service.SetHqOnly(entry, hq);
            }

            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Only buy high quality");
            }
        }
        else
        {
            ImGui.AlignTextToFramePadding();
            UiFormat.CenteredText("–", AuxTheme.MutedFg);
        }
    }

    private void DrawStatus(ShoppingEntry entry, MarketSnapshot snapshot, bool busy, bool covered, ItemSummary? summary)
    {
        var offset = (ImGui.GetFrameHeight() - UiFormat.ChipHeight()) / 2f;
        if (offset > 0f)
        {
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() + offset);
        }

        if (busy && !covered)
        {
            AuxWidgets.StatusChip("Updating", false);
            return;
        }

        var hasListings = snapshot.Markets.TryGetValue(entry.ItemId, out var market) && market.Listings.Count > 0;
        if (summary is null || !hasListings)
        {
            AuxWidgets.StatusChip("No data", false);
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("No listings found in this scope");
            }

            return;
        }

        if (!covered)
        {
            AuxWidgets.StatusChip("Stale", false);
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Refresh to price the current quantity");
            }

            return;
        }

        if (summary.Short > 0)
        {
            AuxWidgets.StatusChip("Short", false);
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip($"Only {UiFormat.Number(summary.Bought)} of {UiFormat.Number(summary.Needed)} available");
            }

            return;
        }

        AuxWidgets.StatusChip("OK", true, "Prices are up to date");
    }

    private void DrawFooter()
    {
        var snapshot = service.Snapshot;
        var startX = ImGui.GetCursorPosX();
        var available = ImGui.GetContentRegionAvail().X;
        var buttonWidth = AuxWidgets.TextButtonWidth("Clear list");

        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(AuxTheme.MutedFg, "Cheapest total");
        ImGui.SameLine();
        using (AuxFonts.PushBold())
        {
            ImGui.TextUnformatted(UiFormat.Gil(snapshot.Plan.CheapestTotal));
        }

        ImGui.SameLine(startX + available - buttonWidth);
        if (AuxWidgets.TextButton("Clear list") && ImGui.GetIO().KeyShift)
        {
            service.Clear();
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Hold Shift and click to clear the whole list");
        }
    }
}
