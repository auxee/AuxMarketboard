using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.GameFonts;
using Dalamud.Interface.ManagedFontAtlas;
using Dalamud.Plugin;
using System;
using System.IO;
using System.Text.Unicode;

namespace AuxUi;

public static class AuxFonts
{
    private const string FontFolder = "Fonts";
    private const string RegularFile = "Lato-Regular.ttf";
    private const string BoldFile = "Lato-Bold.ttf";

    private static IFontHandle? body;
    private static IFontHandle? bold;
    private static IFontHandle? smallBold;

    public static bool IsReady => body is { Available: true } && bold is { Available: true } && smallBold is { Available: true };

    public static void Initialize(IDalamudPluginInterface pluginInterface)
    {
        Dispose();

        var directory = pluginInterface.AssemblyLocation.DirectoryName ?? string.Empty;
        var regularPath = Path.Combine(directory, FontFolder, RegularFile);
        var boldPath = Path.Combine(directory, FontFolder, BoldFile);
        var atlas = pluginInterface.UiBuilder.FontAtlas;

        body = atlas.NewDelegateFontHandle(build => build.OnPreBuild(toolkit =>
            BuildFont(toolkit, regularPath, pluginInterface.UiBuilder.FontDefaultSizePx)));

        bold = atlas.NewDelegateFontHandle(build => build.OnPreBuild(toolkit =>
            BuildFont(toolkit, boldPath, pluginInterface.UiBuilder.FontDefaultSizePx)));

        smallBold = atlas.NewDelegateFontHandle(build => build.OnPreBuild(toolkit =>
            BuildFont(toolkit, boldPath, pluginInterface.UiBuilder.FontDefaultSizePx * AuxTheme.SmallFontRatio)));
    }

    private static void BuildFont(IFontAtlasBuildToolkitPreBuild toolkit, string path, float sizePx)
    {
        var lato = toolkit.AddFontFromFile(path, new SafeFontConfig
        {
            SizePx = sizePx,
            GlyphRanges = LatinRanges(),
        });
        toolkit.AddGameGlyphs(new GameFontStyle(GameFontFamily.Axis, sizePx), FallbackRanges(), lato);
        toolkit.AddGameSymbol(new SafeFontConfig { SizePx = sizePx, MergeFont = lato });
        toolkit.AttachExtraGlyphsForDalamudLanguage(new SafeFontConfig { SizePx = sizePx, MergeFont = lato });
        toolkit.Font = lato;
    }

    public static void Dispose()
    {
        body?.Dispose();
        bold?.Dispose();
        smallBold?.Dispose();
        body = null;
        bold = null;
        smallBold = null;
    }

    public static IDisposable PushBody() => Push(body);

    public static IDisposable PushBold() => Push(bold);

    public static IDisposable PushSmallBold() => Push(smallBold);

    private static IDisposable Push(IFontHandle? handle) => handle?.Push() ?? NoFont.Instance;

    private static ushort[] LatinRanges() => new FluentGlyphRangeBuilder()
        .With(UnicodeRanges.BasicLatin)
        .With(UnicodeRanges.Latin1Supplement)
        .With(UnicodeRanges.LatinExtendedA)
        .With(UnicodeRanges.LatinExtendedB)
        .With(UnicodeRanges.LatinExtendedAdditional)
        .With(UnicodeRanges.GeneralPunctuation)
        .With(UnicodeRanges.CurrencySymbols)
        .With(UnicodeRanges.Arrows)
        .BuildExact();

    private static ushort[] FallbackRanges() => new FluentGlyphRangeBuilder()
        .With(UnicodeRanges.GreekandCoptic)
        .With(UnicodeRanges.Cyrillic)
        .With(UnicodeRanges.Hiragana)
        .With(UnicodeRanges.Katakana)
        .With(UnicodeRanges.CjkSymbolsandPunctuation)
        .With(UnicodeRanges.HalfwidthandFullwidthForms)
        .With(UnicodeRanges.CjkUnifiedIdeographs)
        .BuildExact();

    private sealed class NoFont : IDisposable
    {
        public static readonly NoFont Instance = new();

        public void Dispose()
        {
        }
    }
}
