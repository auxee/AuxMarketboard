using Dalamud.Bindings.ImGui;
using AuxMarketboard.Services;
using AuxUi;

namespace AuxMarketboard.UI;

internal sealed class ImportPanel
{
    private const int MaxImportLength = 100_000;

    private static readonly string[] HintCandidates =
    {
        "Teamcraft link, Artisan JSON or lines like 'Item x3'",
        "Link, JSON or lines like 'Item x3'",
        "Link, JSON or item lines",
        "Paste a list",
    };

    private readonly MarketService service;
    private readonly ListImporter importer;

    private string importText = string.Empty;
    private string message = string.Empty;

    public ImportPanel(MarketService service, ListImporter importer)
    {
        this.service = service;
        this.importer = importer;
    }

    public void Draw()
    {
        ImGui.PushID("import");
        AuxWidgets.SectionLabel("Import");

        var spacing = AuxTheme.ItemSpacing.X;
        var pasteWidth = AuxWidgets.TextButtonWidth("Paste");
        var importWidth = AuxWidgets.TextButtonWidth("Import");
        var inputWidth = Math.Max(160f * UiFormat.Scale, ImGui.GetContentRegionAvail().X - pasteWidth - importWidth - spacing * 2f);

        ImGui.SetNextItemWidth(inputWidth);
        var hint = UiFormat.FitHint(HintCandidates, inputWidth - AuxTheme.FramePadding.X * 2f);
        AuxWidgets.InputTextWithHint("##importText", hint, ref importText, MaxImportLength);
        var enterPressed = (ImGui.IsItemActive() || ImGui.IsItemDeactivated())
            && (ImGui.IsKeyPressed(ImGuiKey.Enter, false) || ImGui.IsKeyPressed(ImGuiKey.KeypadEnter, false));

        ImGui.SameLine();
        if (AuxWidgets.TextButton("Paste"))
        {
            importText = ImGui.GetClipboardText() ?? string.Empty;
        }

        ImGui.SameLine();
        var importPressed = AuxWidgets.TextButton("Import", true);

        if (importPressed || enterPressed)
        {
            Run();
        }

        if (message.Length > 0)
        {
            ImGui.TextColored(AuxTheme.MutedFg, message);
        }

        ImGui.PopID();
    }

    private void Run()
    {
        var result = importer.Parse(importText);
        service.AddItems(result.Items);
        message = result.Message;
        if (result.Items.Count > 0)
        {
            importText = string.Empty;
        }
    }
}
