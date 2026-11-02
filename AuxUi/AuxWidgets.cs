using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using System;
using System.Numerics;

namespace AuxUi;

public enum TableAlign { Left, Center, Right }

public readonly record struct TitleBarButton(FontAwesomeIcon Icon, string Tooltip);

public readonly record struct TitleBarAction(string Label, bool Primary = false);

public readonly record struct TitleBarResult(int Action, int Button);

public static class AuxWidgets
{
    public static readonly TitleBarButton CloseButton = new(FontAwesomeIcon.Times, "Close");

    private const float HintIconWidth = 16f;
    private const float TabGap = 4f;
    private const float TabPaddingX = 14f;
    private const float TextButtonPaddingX = 14f;
    private const float ChipPaddingX = 8f;
    private const float ChipPaddingY = 3f;
    private const float MarkerWidth = 3f;
    private const float MarkerHeight = 12f;

    private static float Scale => AuxTheme.Scale;

    public static TitleBarResult TitleBar(string title, TitleBarAction[] actions, params TitleBarButton[] buttons)
    {
        var windowPosition = ImGui.GetWindowPos();
        var windowWidth = ImGui.GetWindowWidth();
        var height = AuxTheme.TitleBarHeight * Scale;
        var drawList = ImGui.GetWindowDrawList();

        drawList.AddRectFilled(windowPosition, windowPosition + new Vector2(windowWidth, height), ImGui.GetColorU32(AuxTheme.NavBg), AuxTheme.WindowRounding * Scale, ImDrawFlags.RoundCornersTop);

        var dotRadius = 4f * Scale;
        drawList.AddCircleFilled(new Vector2(windowPosition.X + 16f * Scale, windowPosition.Y + height / 2f), dotRadius, ImGui.GetColorU32(AuxTheme.Accent));

        using (AuxFonts.PushBold())
        {
            var textSize = ImGui.CalcTextSize(title);
            var textPosition = new Vector2(windowPosition.X + 30f * Scale, windowPosition.Y + MathF.Floor((height - textSize.Y) / 2f));
            drawList.AddText(textPosition, ImGui.GetColorU32(AuxTheme.NavFg), title);
        }

        var frameHeight = ImGui.GetFrameHeight();
        var top = windowPosition.Y + MathF.Floor((height - frameHeight) / 2f);
        var spacing = AuxTheme.ItemSpacing.X;
        var right = windowPosition.X + windowWidth - 10f * Scale;
        var clickedButton = -1;
        var clickedAction = -1;

        for (var i = buttons.Length - 1; i >= 0; i--)
        {
            right -= frameHeight;
            ImGui.SetCursorScreenPos(new Vector2(right, top));
            if (IconButton(buttons[i].Icon, $"titleBar{i}", buttons[i].Tooltip, false, new Vector2(frameHeight)))
            {
                clickedButton = i;
            }

            right -= spacing;
        }

        for (var i = actions.Length - 1; i >= 0; i--)
        {
            var width = TextButtonWidth(actions[i].Label);
            right -= width;
            ImGui.SetCursorScreenPos(new Vector2(right, top));
            if (TextButton(actions[i].Label, actions[i].Primary))
            {
                clickedAction = i;
            }

            right -= spacing;
        }

        ImGui.SetCursorPos(new Vector2(AuxTheme.WindowPadding.X, height + 8f * Scale));
        return new TitleBarResult(clickedAction, clickedButton);
    }

    public static int Tabs(string id, string[] labels, int selected)
    {
        ImGui.PushID(id);
        var drawList = ImGui.GetWindowDrawList();
        var start = ImGui.GetCursorScreenPos();
        var available = ImGui.GetContentRegionAvail().X;
        var height = ImGui.GetFrameHeight();
        var x = start.X;

        for (var i = 0; i < labels.Length; i++)
        {
            var textSize = ImGui.CalcTextSize(labels[i]);
            var width = textSize.X + TabPaddingX * 2f * Scale;
            ImGui.SetCursorScreenPos(new Vector2(x, start.Y));
            if (ImGui.InvisibleButton($"##tab{i}", new Vector2(width, height)))
            {
                selected = i;
            }

            var hovered = ImGui.IsItemHovered();
            var min = new Vector2(x, start.Y);
            var max = new Vector2(x + width, start.Y + height);
            if (i == selected)
            {
                drawList.AddRectFilled(min, max, ImGui.GetColorU32(AuxTheme.Accent), height / 4f);
            }
            else if (hovered)
            {
                drawList.AddRectFilled(min, max, ImGui.GetColorU32(AuxTheme.HoverTint), height / 4f);
            }

            var color = i == selected ? AuxTheme.OnAccent : AuxTheme.MutedFg;
            var textPosition = new Vector2(x + TabPaddingX * Scale, start.Y + MathF.Floor((height - textSize.Y) / 2f));
            drawList.AddText(textPosition, ImGui.GetColorU32(color), labels[i]);

            x += width + TabGap * Scale;
        }

        ImGui.SetCursorScreenPos(start);
        ImGui.Dummy(new Vector2(available, height));
        ImGui.PopID();
        return selected;
    }

    public static void SectionLabel(string label, string? hint = null, float reserveRight = 0f)
    {
        var start = ImGui.GetCursorScreenPos();
        var rightEdge = start.X + ImGui.GetContentRegionAvail().X - reserveRight;
        var markerSpace = (MarkerWidth + 6f) * Scale;
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + markerSpace);
        using (AuxFonts.PushSmallBold())
        {
            ImGui.TextColored(AuxTheme.MutedFg, label.ToUpperInvariant());
        }

        var labelMin = ImGui.GetItemRectMin();
        var labelMax = ImGui.GetItemRectMax();
        var centerY = MathF.Floor((labelMin.Y + labelMax.Y) / 2f);
        var drawList = ImGui.GetWindowDrawList();
        var markerHalf = MarkerHeight * Scale / 2f;
        drawList.AddRectFilled(new Vector2(start.X, centerY - markerHalf), new Vector2(start.X + MarkerWidth * Scale, centerY + markerHalf), ImGui.GetColorU32(AuxTheme.Accent), MarkerWidth * Scale / 2f);

        var lineStart = labelMax.X;
        if (hint != null)
        {
            ImGui.SameLine();
            Hint(hint);
            lineStart = ImGui.GetItemRectMax().X;
        }

        lineStart += 8f * Scale;
        if (rightEdge > lineStart)
        {
            drawList.AddLine(new Vector2(lineStart, centerY + 0.5f), new Vector2(rightEdge, centerY + 0.5f), ImGui.GetColorU32(AuxTheme.Rule));
        }
    }

    public static float TextButtonWidth(string label) => ImGui.CalcTextSize(label).X + TextButtonPaddingX * 2f * Scale;

    public static bool TextButton(string label, bool primary = false)
    {
        using var colors = ImRaii.PushColor(ImGuiCol.Text, primary ? AuxTheme.OnAccent : AuxTheme.NavFg)
            .Push(ImGuiCol.Button, primary ? AuxTheme.Accent : AuxTheme.Transparent)
            .Push(ImGuiCol.ButtonHovered, primary ? AuxTheme.AccentHover : AuxTheme.HoverTint)
            .Push(ImGuiCol.ButtonActive, primary ? AuxTheme.AccentDeep : AuxTheme.ActiveTint);
        using var styles = ImRaii.PushStyle(ImGuiStyleVar.FramePadding, new Vector2(TextButtonPaddingX * Scale, AuxTheme.FramePadding.Y))
            .Push(ImGuiStyleVar.FrameRounding, ImGui.GetFrameHeight() / 4f);
        return ImGui.Button(label);
    }

    public static bool IconButton(FontAwesomeIcon icon, string id, string? tooltip = null, bool danger = false, Vector2? size = null)
    {
        var buttonSize = size ?? new Vector2(ImGui.GetFrameHeight());
        var glyph = ((char)icon).ToString();
        bool pressed;
        bool hovered;

        using (ImRaii.PushFont(UiBuilder.IconFont))
        using (ImRaii.PushStyle(ImGuiStyleVar.FrameRounding, Math.Min(buttonSize.X, buttonSize.Y) / 4f))
        using (ImRaii.PushColor(ImGuiCol.Button, AuxTheme.Transparent)
            .Push(ImGuiCol.ButtonHovered, AuxTheme.HoverTint)
            .Push(ImGuiCol.ButtonActive, AuxTheme.ActiveTint))
        {
            pressed = ImGui.Button($"##{id}", buttonSize);
            hovered = ImGui.IsItemHovered();
            var min = ImGui.GetItemRectMin();
            var textSize = ImGui.CalcTextSize(glyph);
            var color = hovered ? (danger ? AuxTheme.Danger : AuxTheme.ContentFg) : AuxTheme.NavFg;
            var position = new Vector2(MathF.Floor(min.X + (buttonSize.X - textSize.X) / 2f), MathF.Floor(min.Y + (buttonSize.Y - textSize.Y) / 2f));
            ImGui.GetWindowDrawList().AddText(position, ImGui.GetColorU32(color), glyph);
        }

        if (hovered && tooltip != null)
        {
            ImGui.SetTooltip(tooltip);
        }

        return pressed;
    }

    public static bool Checkbox(string label, ref bool value)
    {
        var isChecked = value;
        using var colors = ImRaii.PushColor(ImGuiCol.FrameBg, isChecked ? AuxTheme.Accent : AuxTheme.CellBg)
            .Push(ImGuiCol.FrameBgHovered, isChecked ? AuxTheme.AccentHover : AuxTheme.HoverTint)
            .Push(ImGuiCol.FrameBgActive, isChecked ? AuxTheme.AccentDeep : AuxTheme.ActiveTint)
            .Push(ImGuiCol.CheckMark, AuxTheme.OnAccent);
        using var rounding = ImRaii.PushStyle(ImGuiStyleVar.FrameRounding, AuxTheme.CheckboxRounding * Scale);
        return ImGui.Checkbox(label, ref value);
    }

    public static void Hint(string text)
    {
        var glyph = ((char)FontAwesomeIcon.QuestionCircle).ToString();
        bool hovered;

        using (ImRaii.PushFont(UiBuilder.IconFont))
        {
            var size = ImGui.CalcTextSize(glyph);
            var position = ImGui.GetCursorScreenPos();
            hovered = ImGui.IsMouseHoveringRect(position, position + size);
            ImGui.TextColored(hovered ? AuxTheme.NavFg : AuxTheme.MutedFg, glyph);
        }

        if (ImGui.IsItemHovered())
        {
            using (ImRaii.Tooltip())
            {
                ImGui.TextUnformatted(text);
            }
        }
    }

    public static bool InputText(string label, ref string value, int maxLength)
    {
        var changed = ImGui.InputText(label, ref value, maxLength);
        DrawItemFocusOutline();
        return changed;
    }

    public static bool InputTextWithHint(string label, string hint, ref string value, int maxLength)
    {
        var changed = ImGui.InputTextWithHint(label, hint, ref value, maxLength);
        DrawItemFocusOutline();
        return changed;
    }

    public static bool InputInt(string label, ref int value)
    {
        var changed = ImGui.InputInt(label, ref value);
        DrawItemFocusOutline();
        return changed;
    }

    public static bool DragInt(string label, ref int value, float speed, int min, int max)
    {
        var changed = ImGui.DragInt(label, ref value, speed, min, max);
        DrawItemFocusOutline();
        return changed;
    }

    public static bool DragFloat(string label, ref float value, float speed, float min, float max)
    {
        var changed = ImGui.DragFloat(label, ref value, speed, min, max);
        DrawItemFocusOutline();
        return changed;
    }

    public static bool BeginCombo(string label, string preview, ImGuiComboFlags flags = ImGuiComboFlags.None)
    {
        var drawList = ImGui.GetWindowDrawList();
        var min = ImGui.GetCursorScreenPos();
        var max = min + new Vector2(ImGui.CalcItemWidth(), ImGui.GetFrameHeight());
        var open = ImGui.BeginCombo(label, preview, flags);
        if (open || ImGui.IsItemActive())
        {
            DrawFocusOutline(drawList, min, max);
        }

        return open;
    }

    public static bool RoundedSelectable(string label, bool selected, Vector2? size = null, float textInset = 0f, float fillPadY = 0f)
    {
        var width = size?.X > 0f ? size.Value.X : ImGui.GetContentRegionAvail().X;
        var height = size?.Y > 0f ? size.Value.Y : ImGui.GetTextLineHeight();
        var inset = textInset * Scale;
        var rowMin = ImGui.GetCursorScreenPos();
        var rowRight = rowMin.X + width;
        var drawList = ImGui.GetWindowDrawList();
        bool pressed;

        drawList.ChannelsSplit(2);
        drawList.ChannelsSetCurrent(1);
        ImGui.SetCursorScreenPos(new Vector2(rowMin.X + inset, rowMin.Y));
        using (ImRaii.PushColor(ImGuiCol.Header, AuxTheme.Transparent)
            .Push(ImGuiCol.HeaderHovered, AuxTheme.Transparent)
            .Push(ImGuiCol.HeaderActive, AuxTheme.Transparent))
        using (ImRaii.PushStyle(ImGuiStyleVar.SelectableTextAlign, new Vector2(0f, 0.5f)))
        {
            pressed = ImGui.Selectable(label, selected, ImGuiSelectableFlags.None, new Vector2(width - inset, height));
        }

        var hovered = ImGui.IsItemHovered();
        var spacingUp = MathF.Floor(ImGui.GetStyle().ItemSpacing.Y * 0.5f);
        var spacingDown = ImGui.GetStyle().ItemSpacing.Y - spacingUp;
        var bandTop = ImGui.GetItemRectMin().Y + spacingUp - fillPadY;
        var bandBottom = ImGui.GetItemRectMax().Y - spacingDown + fillPadY;
        drawList.ChannelsSetCurrent(0);
        var windowPosition = ImGui.GetWindowPos();
        var contentMin = windowPosition + ImGui.GetWindowContentRegionMin();
        var contentMax = windowPosition + ImGui.GetWindowContentRegionMax();
        var fillMin = new Vector2(Math.Max(rowMin.X, contentMin.X), Math.Max(bandTop, contentMin.Y));
        var fillMax = new Vector2(Math.Min(rowRight, contentMax.X), bandBottom);
        var rounding = AuxTheme.FrameRounding * Scale;
        if (selected)
        {
            drawList.AddRectFilled(fillMin, fillMax, ImGui.GetColorU32(AuxTheme.AccentSelection), rounding);
        }
        else if (hovered)
        {
            drawList.AddRectFilled(fillMin, fillMax, ImGui.GetColorU32(AuxTheme.HoverTint), rounding);
        }

        drawList.ChannelsMerge();
        return pressed;
    }

    public static bool ComboItem(string label, bool selected) => RoundedSelectable(label, selected);

    public static bool BeginTable(string id, int columns, bool scrollingRows = false, bool padOuter = false)
    {
        var flags = scrollingRows ? ImGuiTableFlags.ScrollY | ImGuiTableFlags.RowBg : ImGuiTableFlags.None;
        if (padOuter)
        {
            flags |= ImGuiTableFlags.PadOuterX;
        }

        return ImGui.BeginTable(id, columns, flags);
    }

    public static void TableHeaderRow(string id, string[] hints, params TableAlign[] aligns)
    {
        ImGui.TableNextRow();
        var drawList = ImGui.GetWindowDrawList();
        var columns = ImGui.TableGetColumnCount();
        var lineLeft = 0f;
        var lineRight = 0f;
        var lineY = 0f;

        for (var i = 0; i < columns; i++)
        {
            ImGui.TableNextColumn();
            var label = ImGui.TableGetColumnName(i).ToUpperInvariant();
            var align = aligns.Length <= i ? TableAlign.Left : aligns[i];
            var cellStartX = ImGui.GetCursorPosX();
            var cellAvailable = ImGui.GetContentRegionAvail().X;
            var cellScreen = ImGui.GetCursorScreenPos();

            if (i == 0)
            {
                lineLeft = cellScreen.X - AuxTheme.CellPadding.X;
            }

            if (i == columns - 1)
            {
                lineRight = cellScreen.X + cellAvailable + AuxTheme.CellPadding.X;
            }

            ImGui.PushID($"{id}Header_{i}");
            using (AuxFonts.PushSmallBold())
            using (ImRaii.PushColor(ImGuiCol.Text, AuxTheme.MutedFg))
            {
                var textWidth = ImGui.CalcTextSize(label).X;
                switch (align)
                {
                    case TableAlign.Center:
                        ImGui.SetCursorPosX(cellStartX + Math.Max(0f, (cellAvailable - textWidth) / 2f));
                        break;
                    case TableAlign.Right:
                        ImGui.SetCursorPosX(cellStartX + Math.Max(0f, cellAvailable - textWidth));
                        break;
                }

                ImGui.TextUnformatted(label);
            }

            lineY = Math.Max(lineY, ImGui.GetItemRectMax().Y + AuxTheme.CellPadding.Y);

            if (i < hints.Length && hints[i].Length > 0)
            {
                ImGui.SameLine(cellStartX + cellAvailable - HintIconWidth * Scale);
                Hint(hints[i]);
            }

            ImGui.PopID();
        }

        drawList.PushClipRect(new Vector2(lineLeft, lineY - 1f), new Vector2(lineRight, lineY + 1f), false);
        drawList.AddLine(new Vector2(lineLeft, lineY - 0.5f), new Vector2(lineRight, lineY - 0.5f), ImGui.GetColorU32(AuxTheme.Rule));
        drawList.PopClipRect();
    }

    public static void Gauge(float fraction, Vector4 color, float width = 0f, float height = 0f)
    {
        var min = ImGui.GetCursorScreenPos();
        var size = new Vector2(width > 0f ? width : ImGui.GetContentRegionAvail().X, height > 0f ? height : ImGui.GetFrameHeight());
        Gauge(min, min + size, fraction, color);
    }

    public static void Gauge(Vector2 min, Vector2 max, float fraction, Vector4 color)
    {
        var (fillMin, fillMax) = AuxGauge.FillRect(min, max, fraction);
        if (fillMax.X > fillMin.X)
        {
            var radius = Math.Min((fillMax.Y - fillMin.Y) / 4f, (fillMax.X - fillMin.X) / 4f);
            ImGui.GetWindowDrawList().AddRectFilled(fillMin, fillMax, ImGui.GetColorU32(color), radius);
        }
    }

    public static void NavLabel(string text)
    {
        using (AuxFonts.PushSmallBold())
        {
            var upper = text.ToUpperInvariant();
            var textSize = ImGui.CalcTextSize(upper);
            var padding = new Vector2(ChipPaddingX, ChipPaddingY) * Scale;
            var position = ImGui.GetCursorScreenPos();
            ImGui.Dummy(textSize + padding * 2f);
            ImGui.GetWindowDrawList().AddText(position + padding, ImGui.GetColorU32(AuxTheme.MutedFg), upper);
        }
    }

    public static void StatusChip(string label, bool enabled, string? tooltip = null)
    {
        using (AuxFonts.PushSmallBold())
        {
            var text = label.ToUpperInvariant();
            var textSize = ImGui.CalcTextSize(text);
            var padding = new Vector2(ChipPaddingX, ChipPaddingY) * Scale;
            var position = ImGui.GetCursorScreenPos();
            var size = textSize + padding * 2f;
            ImGui.Dummy(size);
            var hovered = ImGui.IsItemHovered();

            var drawList = ImGui.GetWindowDrawList();
            drawList.AddRectFilled(position, position + size, ImGui.GetColorU32(enabled ? AuxTheme.Accent : AuxTheme.CellBg), size.Y / 4f);
            drawList.AddText(position + padding, ImGui.GetColorU32(enabled ? AuxTheme.OnAccent : AuxTheme.MutedFg), text);

            if (enabled && hovered && tooltip != null)
            {
                ImGui.SetTooltip(tooltip);
            }
        }
    }

    private static void DrawItemFocusOutline()
    {
        if (ImGui.IsItemActive() || ImGui.IsItemFocused())
        {
            DrawFocusOutline(ImGui.GetWindowDrawList(), ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
        }
    }

    private static void DrawFocusOutline(ImDrawListPtr drawList, Vector2 min, Vector2 max)
    {
        drawList.AddRect(min, max, ImGui.GetColorU32(AuxTheme.AccentHover), AuxTheme.FrameRounding * Scale, ImDrawFlags.None, 1f);
    }
}
