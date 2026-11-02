using System.Globalization;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using AuxUi;

namespace AuxMarketboard.UI;

internal static class UiFormat
{
    private const float TabPaddingX = 14f;
    private const float TabGap = 4f;
    private const float ChipPaddingY = 3f;
    private const string Ellipsis = "…";

    public static float Scale => ImGuiHelpers.GlobalScale;

    public static string Number(long value) => value.ToString("N0", CultureInfo.InvariantCulture);

    public static string Gil(long value) => $"{Number(value)} gil";

    public static string Ago(DateTime utc)
    {
        var elapsed = DateTime.UtcNow - utc;
        if (elapsed < TimeSpan.FromSeconds(10))
        {
            return "just now";
        }

        if (elapsed < TimeSpan.FromMinutes(1))
        {
            return $"{(int)elapsed.TotalSeconds} s ago";
        }

        if (elapsed < TimeSpan.FromHours(1))
        {
            return $"{(int)elapsed.TotalMinutes} min ago";
        }

        return $"{(int)elapsed.TotalHours} h ago";
    }

    public static string Ellipsize(string text, float maxWidth)
    {
        if (ImGui.CalcTextSize(text).X <= maxWidth)
        {
            return text;
        }

        var cut = text;
        while (cut.Length > 1 && ImGui.CalcTextSize(cut + Ellipsis).X > maxWidth)
        {
            cut = cut[..^1];
        }

        return cut + Ellipsis;
    }

    public static string FitHint(string[] candidates, float innerWidth)
    {
        foreach (var candidate in candidates)
        {
            if (ImGui.CalcTextSize(candidate).X <= innerWidth)
            {
                return candidate;
            }
        }

        return candidates[^1];
    }

    public static float TabsWidth(string[] labels)
    {
        var total = 0f;
        foreach (var label in labels)
        {
            total += ImGui.CalcTextSize(label).X + TabPaddingX * 2f * Scale + TabGap * Scale;
        }

        return Math.Max(0f, total - TabGap * Scale);
    }

    public static float ChipHeight()
    {
        using (AuxFonts.PushSmallBold())
        {
            return ImGui.GetTextLineHeight() + ChipPaddingY * 2f * Scale;
        }
    }

    public static void AddText(Vector2 position, Vector4 color, string text)
    {
        ImGui.GetWindowDrawList().AddText(position, ImGui.GetColorU32(color), text);
    }

    public static float CenteredTextY(float top, float height)
        => top + MathF.Floor((height - ImGui.GetTextLineHeight()) / 2f);

    public static void RightAlignedText(string text, Vector4 color)
    {
        var width = ImGui.CalcTextSize(text).X;
        var available = ImGui.GetContentRegionAvail().X;
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + Math.Max(0f, available - width));
        ImGui.TextColored(color, text);
    }

    public static void CenteredText(string text, Vector4 color)
    {
        var width = ImGui.CalcTextSize(text).X;
        var available = ImGui.GetContentRegionAvail().X;
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + Math.Max(0f, (available - width) / 2f));
        ImGui.TextColored(color, text);
    }

    public static void RowGauges(IReadOnlyList<float> fractions)
    {
        var origin = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var rowHeight = TableRowHeight();
        var top = origin.Y + TableHeaderHeight();
        var inset = Scale;
        var color = AuxTheme.WithAlpha(AuxTheme.Accent, 0.30f);
        for (var i = 0; i < fractions.Count; i++)
        {
            if (fractions[i] <= 0f)
            {
                continue;
            }

            var rowTop = top + i * rowHeight;
            AuxWidgets.Gauge(
                new Vector2(origin.X, rowTop + inset),
                new Vector2(origin.X + width, rowTop + rowHeight - inset),
                fractions[i],
                color);
        }
    }

    public static float TableHeaderHeight()
    {
        using (AuxFonts.PushSmallBold())
        {
            return ImGui.GetTextLineHeight() + AuxTheme.CellPadding.Y * 2f;
        }
    }

    public static float TableRowHeight() => ImGui.GetFrameHeight() + AuxTheme.CellPadding.Y * 2f;
}
