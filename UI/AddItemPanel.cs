using System.Numerics;
using Dalamud.Bindings.ImGui;
using AuxMarketboard.Services;
using AuxUi;

namespace AuxMarketboard.UI;

internal sealed class AddItemPanel
{
    private const string ResultsPopupId = "##itemSearchResults";
    private const int MaxMatches = 25;
    private const int MaxSearchLength = 64;

    private static readonly string[] HintCandidates = { "Search marketable items", "Search items", "Search" };

    private readonly MarketService service;
    private readonly ItemResolver resolver;

    private string searchText = string.Empty;
    private string matchedFor = string.Empty;
    private IReadOnlyList<(uint Id, string Name)> matches = Array.Empty<(uint, string)>();
    private uint selectedId;
    private int quantity = 1;
    private bool refocusSearch;

    public AddItemPanel(MarketService service, ItemResolver resolver)
    {
        this.service = service;
        this.resolver = resolver;
    }

    public void Draw()
    {
        ImGui.PushID("addItem");
        AuxWidgets.SectionLabel("Add item");

        var scale = UiFormat.Scale;
        var spacing = AuxTheme.ItemSpacing.X;
        var quantityWidth = 130f * scale;
        var addWidth = AuxWidgets.TextButtonWidth("Add");
        var searchWidth = Math.Max(160f * scale, ImGui.GetContentRegionAvail().X - quantityWidth - addWidth - spacing * 2f);

        if (refocusSearch)
        {
            ImGui.SetKeyboardFocusHere();
            refocusSearch = false;
        }

        ImGui.SetNextItemWidth(searchWidth);
        var hint = UiFormat.FitHint(HintCandidates, searchWidth - AuxTheme.FramePadding.X * 2f);
        if (AuxWidgets.InputTextWithHint("##search", hint, ref searchText, MaxSearchLength))
        {
            selectedId = 0;
        }

        var searchActive = ImGui.IsItemActive();
        var enterPressed = (searchActive || ImGui.IsItemDeactivated())
            && (ImGui.IsKeyPressed(ImGuiKey.Enter, false) || ImGui.IsKeyPressed(ImGuiKey.KeypadEnter, false));
        var searchMin = ImGui.GetItemRectMin();
        var searchMax = ImGui.GetItemRectMax();

        RefreshMatches();
        var showResults = searchText.Trim().Length > 0 && selectedId == 0 && matches.Count > 0;
        if (showResults && searchActive && !ImGui.IsPopupOpen(ResultsPopupId))
        {
            ImGui.OpenPopup(ResultsPopupId);
        }

        ImGui.SameLine();
        ImGui.SetNextItemWidth(quantityWidth);
        AuxWidgets.InputInt("##quantity", ref quantity);
        quantity = Math.Clamp(quantity, 1, Configuration.MaxQuantity);

        ImGui.SameLine();
        var addPressed = AuxWidgets.TextButton("Add", true);

        DrawResults(showResults, searchMin, searchMax);

        if (addPressed || enterPressed)
        {
            AddSelection();
        }

        ImGui.PopID();
    }

    private void RefreshMatches()
    {
        if (searchText == matchedFor)
        {
            return;
        }

        matchedFor = searchText;
        matches = searchText.Trim().Length == 0
            ? Array.Empty<(uint, string)>()
            : resolver.GetSearchMatches(searchText, MaxMatches);
    }

    private void DrawResults(bool show, Vector2 searchMin, Vector2 searchMax)
    {
        if (!ImGui.IsPopupOpen(ResultsPopupId))
        {
            return;
        }

        var width = searchMax.X - searchMin.X;
        ImGui.SetNextWindowPos(new Vector2(searchMin.X, searchMax.Y + 2f * UiFormat.Scale));
        ImGui.SetNextWindowSizeConstraints(new Vector2(width, 0f), new Vector2(width, 320f * UiFormat.Scale));
        var flags = ImGuiWindowFlags.NoFocusOnAppearing
            | ImGuiWindowFlags.NoMove
            | ImGuiWindowFlags.NoSavedSettings
            | ImGuiWindowFlags.NoTitleBar
            | ImGuiWindowFlags.AlwaysAutoResize;

        if (!ImGui.BeginPopup(ResultsPopupId, flags))
        {
            return;
        }

        if (!show)
        {
            ImGui.CloseCurrentPopup();
        }
        else
        {
            var rowPadding = 2f * UiFormat.Scale;
            for (var i = 0; i < matches.Count; i++)
            {
                var match = matches[i];
                if (AuxWidgets.RoundedSelectable($"{match.Name}##match{i}", match.Id == selectedId, null, 0f, rowPadding))
                {
                    selectedId = match.Id;
                    searchText = match.Name;
                    matchedFor = match.Name;
                    ImGui.CloseCurrentPopup();
                }
            }
        }

        ImGui.EndPopup();
    }

    private void AddSelection()
    {
        var id = selectedId;
        if (id == 0 && matches.Count > 0)
        {
            id = matches[0].Id;
        }

        if (id == 0)
        {
            return;
        }

        service.AddItem(id, quantity);
        searchText = string.Empty;
        matchedFor = string.Empty;
        matches = Array.Empty<(uint, string)>();
        selectedId = 0;
        refocusSearch = true;
    }
}
