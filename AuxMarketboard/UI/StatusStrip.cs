using System.Numerics;
using Dalamud.Bindings.ImGui;
using AuxMarketboard.Services;
using AuxUi;

namespace AuxMarketboard.UI;

internal static class StatusStrip
{
    private static readonly string[] ScopeLabels = { "World", "Data Center", "Region" };

    public static void Draw(MarketService service)
    {
        var start = ImGui.GetCursorScreenPos();
        var available = ImGui.GetContentRegionAvail().X;
        var height = ImGui.GetFrameHeight();

        var selected = AuxWidgets.Tabs("##scopeTabs", ScopeLabels, (int)service.Mode);
        if (selected != (int)service.Mode)
        {
            service.SetScopeMode((ScopeMode)selected);
        }

        var gap = 14f * UiFormat.Scale;
        var textY = UiFormat.CenteredTextY(start.Y, height);
        var left = start.X + AuxWidgets.TabsWidth(ScopeLabels) + gap;
        var right = start.X + available;

        var scope = service.Scope;
        string name;
        Vector4 nameColor;
        if (scope.IsKnown)
        {
            name = scope.Name;
            nameColor = AuxTheme.ContentFg;
        }
        else
        {
            name = "Log in to detect your world";
            nameColor = AuxTheme.MutedFg;
        }

        using (AuxFonts.PushBold())
        {
            name = UiFormat.Ellipsize(name, Math.Max(40f * UiFormat.Scale, right - left));
            UiFormat.AddText(new Vector2(left, textY), nameColor, name);
            left += ImGui.CalcTextSize(name).X + gap;
        }

        var status = StatusText(service, out var statusColor);
        if (status.Length > 0 && right > left)
        {
            status = UiFormat.Ellipsize(status, right - left);
            var width = ImGui.CalcTextSize(status).X;
            UiFormat.AddText(new Vector2(right - width, textY), statusColor, status);
        }
    }

    private static string StatusText(MarketService service, out Vector4 color)
    {
        color = AuxTheme.MutedFg;
        if (!service.Scope.IsKnown)
        {
            return string.Empty;
        }

        var state = service.State;
        switch (state.Phase)
        {
            case RefreshPhase.Failed:
                color = AuxTheme.Danger;
                return state.Error;
            case RefreshPhase.Fetching:
                return service.IsRateLimited
                    ? "Rate limited, retrying…"
                    : $"Fetching {state.ChunksDone}/{Math.Max(1, state.ChunksTotal)}…";
            case RefreshPhase.Waiting:
                return "Updating…";
        }

        var fetched = service.Snapshot.FetchedUtc;
        return fetched == DateTime.MinValue ? string.Empty : $"Updated {UiFormat.Ago(fetched)}";
    }
}
