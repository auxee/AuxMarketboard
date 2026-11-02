using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using System;
using System.Numerics;

namespace AuxUi;

public static class AuxTheme
{
    public static readonly Vector4 NavBg = Rgba(38, 38, 38, 0.96f);
    public static readonly Vector4 NavFg = Hex(0xDDDDDD);
    public static readonly Vector4 ContentBg = Rgba(48, 48, 48, 0.94f);
    public static readonly Vector4 ContentFg = Hex(0xEEEEEE);
    public static readonly Vector4 MutedFg = Hex(0xEEEEEE, 0.55f);
    public static readonly Vector4 CellBg = Rgba(255, 255, 255, 0.07f);
    public static readonly Vector4 RowAlt = Rgba(255, 255, 255, 0.035f);
    public static readonly Vector4 HoverTint = Rgba(255, 255, 255, 0.08f);
    public static readonly Vector4 ActiveTint = Rgba(255, 255, 255, 0.14f);
    public static readonly Vector4 Rule = Rgba(255, 255, 255, 0.10f);
    public static readonly Vector4 EdgeLine = Rgba(255, 255, 255, 0.06f);
    public static readonly Vector4 DropdownBg = Rgba(58, 58, 58, 0.98f);
    public static readonly Vector4 Accent = Hex(0x2E7D32);
    public static readonly Vector4 AccentHover = Hex(0x388E3C);
    public static readonly Vector4 AccentDeep = Hex(0x1B5E20);
    public static readonly Vector4 AccentSelection = Hex(0x2E7D32, 0.35f);
    public static readonly Vector4 OnAccent = Hex(0xFFFFFF);
    public static readonly Vector4 Danger = Hex(0xEF5350);
    public static readonly Vector4 Transparent = new(0f, 0f, 0f, 0f);

    public const float TitleBarHeight = 36f;
    public const float SmallFontRatio = 0.8f;
    public const float ScrollbarWidth = 8f;
    public const float WindowRounding = 5f;
    public const float ChildRounding = 4f;
    public const float FrameRounding = 3f;
    public const float PopupRounding = 4f;
    public const float GrabRounding = 3f;
    public const float ScrollbarRounding = 3f;
    public const float CheckboxRounding = 2f;

    public static float Scale => ImGuiHelpers.GlobalScale;

    public static Vector2 WindowPadding => new Vector2(12f, 12f) * Scale;
    public static Vector2 FramePadding => new Vector2(10f, 5f) * Scale;
    public static Vector2 ItemSpacing => new Vector2(8f, 6f) * Scale;
    public static Vector2 ItemInnerSpacing => new Vector2(6f, 4f) * Scale;
    public static Vector2 CellPadding => new Vector2(8f, 4f) * Scale;

    public static IDisposable Push() => new ThemeScope();

    public static Vector4 Rgba(int red, int green, int blue, float alpha) => new(red / 255f, green / 255f, blue / 255f, alpha);

    public static Vector4 Hex(uint rgb, float alpha = 1f) => new(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, alpha);

    public static Vector4 WithAlpha(Vector4 color, float alpha) => new(color.X, color.Y, color.Z, alpha);

    private sealed class ThemeScope : IDisposable
    {
        private readonly IDisposable bodyFont;
        private readonly ImRaii.ColorDisposable colors = new();
        private readonly ImRaii.StyleDisposable styles = new();

        public ThemeScope()
        {
            bodyFont = AuxFonts.PushBody();
            PushColors();
            PushStyles();
        }

        public void Dispose()
        {
            styles.Dispose();
            colors.Dispose();
            bodyFont.Dispose();
        }

        private void PushColors()
        {
            colors
                .Push(ImGuiCol.Text, ContentFg)
                .Push(ImGuiCol.TextDisabled, MutedFg)
                .Push(ImGuiCol.TextSelectedBg, WithAlpha(Accent, 0.55f))
                .Push(ImGuiCol.WindowBg, ContentBg)
                .Push(ImGuiCol.ChildBg, Transparent)
                .Push(ImGuiCol.PopupBg, DropdownBg)
                .Push(ImGuiCol.Border, EdgeLine)
                .Push(ImGuiCol.BorderShadow, Transparent)
                .Push(ImGuiCol.FrameBg, CellBg)
                .Push(ImGuiCol.FrameBgHovered, HoverTint)
                .Push(ImGuiCol.FrameBgActive, ActiveTint)
                .Push(ImGuiCol.TitleBg, NavBg)
                .Push(ImGuiCol.TitleBgActive, NavBg)
                .Push(ImGuiCol.TitleBgCollapsed, NavBg)
                .Push(ImGuiCol.MenuBarBg, NavBg)
                .Push(ImGuiCol.Button, Transparent)
                .Push(ImGuiCol.ButtonHovered, HoverTint)
                .Push(ImGuiCol.ButtonActive, ActiveTint)
                .Push(ImGuiCol.Header, AccentSelection)
                .Push(ImGuiCol.HeaderHovered, HoverTint)
                .Push(ImGuiCol.HeaderActive, ActiveTint)
                .Push(ImGuiCol.CheckMark, OnAccent)
                .Push(ImGuiCol.SliderGrab, Accent)
                .Push(ImGuiCol.SliderGrabActive, AccentDeep)
                .Push(ImGuiCol.Separator, Rule)
                .Push(ImGuiCol.SeparatorHovered, AccentHover)
                .Push(ImGuiCol.SeparatorActive, AccentDeep)
                .Push(ImGuiCol.ResizeGrip, Transparent)
                .Push(ImGuiCol.ResizeGripHovered, AccentHover)
                .Push(ImGuiCol.ResizeGripActive, AccentDeep)
                .Push(ImGuiCol.Tab, Transparent)
                .Push(ImGuiCol.TabHovered, HoverTint)
                .Push(ImGuiCol.TabActive, Accent)
                .Push(ImGuiCol.TabUnfocused, Transparent)
                .Push(ImGuiCol.TabUnfocusedActive, Accent)
                .Push(ImGuiCol.TableHeaderBg, Transparent)
                .Push(ImGuiCol.TableBorderStrong, Rule)
                .Push(ImGuiCol.TableBorderLight, Rule)
                .Push(ImGuiCol.TableRowBg, Transparent)
                .Push(ImGuiCol.TableRowBgAlt, RowAlt)
                .Push(ImGuiCol.ScrollbarBg, Transparent)
                .Push(ImGuiCol.ScrollbarGrab, Rgba(255, 255, 255, 0.18f))
                .Push(ImGuiCol.ScrollbarGrabHovered, Rgba(255, 255, 255, 0.28f))
                .Push(ImGuiCol.ScrollbarGrabActive, Accent)
                .Push(ImGuiCol.DragDropTarget, AccentHover)
                .Push(ImGuiCol.NavHighlight, AccentHover);
        }

        private void PushStyles()
        {
            var scale = Scale;
            styles
                .Push(ImGuiStyleVar.WindowRounding, WindowRounding * scale)
                .Push(ImGuiStyleVar.ChildRounding, ChildRounding * scale)
                .Push(ImGuiStyleVar.FrameRounding, FrameRounding * scale)
                .Push(ImGuiStyleVar.PopupRounding, PopupRounding * scale)
                .Push(ImGuiStyleVar.ScrollbarRounding, ScrollbarRounding * scale)
                .Push(ImGuiStyleVar.GrabRounding, GrabRounding * scale)
                .Push(ImGuiStyleVar.TabRounding, FrameRounding * scale)
                .Push(ImGuiStyleVar.WindowBorderSize, 1f)
                .Push(ImGuiStyleVar.ChildBorderSize, 0f)
                .Push(ImGuiStyleVar.FrameBorderSize, 0f)
                .Push(ImGuiStyleVar.PopupBorderSize, 1f)
                .Push(ImGuiStyleVar.ScrollbarSize, ScrollbarWidth * scale)
                .Push(ImGuiStyleVar.WindowPadding, WindowPadding)
                .Push(ImGuiStyleVar.FramePadding, FramePadding)
                .Push(ImGuiStyleVar.ItemSpacing, ItemSpacing)
                .Push(ImGuiStyleVar.ItemInnerSpacing, ItemInnerSpacing)
                .Push(ImGuiStyleVar.CellPadding, CellPadding);
        }
    }
}
