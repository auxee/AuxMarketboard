using System;
using System.Numerics;

namespace AuxUi;

public static class AuxGauge
{
    public static (Vector2 Min, Vector2 Max) FillRect(Vector2 min, Vector2 max, float fraction)
    {
        var clamped = float.IsNaN(fraction) ? 0f : Math.Clamp(fraction, 0f, 1f);
        var width = Math.Max(0f, max.X - min.X);
        return (min, new Vector2(min.X + width * clamped, max.Y));
    }
}
