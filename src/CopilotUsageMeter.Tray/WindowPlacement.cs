using System;

namespace CopilotUsageMeter.Tray;

/// <summary>A window's position and size in device-independent units, kept only in memory.</summary>
internal readonly record struct WindowPlacement(double Left, double Top, double Width, double Height)
{
    /// <summary>
    /// Fits the placement into a work area: the size is limited to the area (and raised to the minimum) and the
    /// window is moved so that it lies fully inside, which also recovers a window left on a removed monitor.
    /// </summary>
    public WindowPlacement Fit(double areaLeft, double areaTop, double areaRight, double areaBottom, double minWidth, double minHeight)
    {
        var width = Math.Min(Math.Max(Width, minWidth), areaRight - areaLeft);
        var height = Math.Min(Math.Max(Height, minHeight), areaBottom - areaTop);
        var left = Math.Min(Math.Max(Left, areaLeft), areaRight - width);
        var top = Math.Min(Math.Max(Top, areaTop), areaBottom - height);
        return new WindowPlacement(left, top, width, height);
    }

    public bool IsUsable => double.IsFinite(Left) && double.IsFinite(Top) && double.IsFinite(Width) && double.IsFinite(Height) && Width > 0 && Height > 0;
}
