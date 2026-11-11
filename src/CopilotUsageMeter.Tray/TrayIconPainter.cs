using System;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace CopilotUsageMeter.Tray;

// Draws the usage ring. The icon is shown at about 16 px, so the arc is thick and bright (cyan) over a thin, light track
// that stays visible on dark taskbars without competing with the arc.
internal static class TrayIconPainter
{
    private const float Design = 32f;
    private const float MinimumVisibleSweep = 14f;

    // Widths are in the 32 px design space; the track may be thinner than the progress arc.
    internal readonly record struct Style(float TrackWidth, float ProgressWidth, Color TrackColor, Color ProgressColor);

    internal static readonly Color Track = Color.FromArgb(255, 185, 190, 196);
    internal static readonly Color Normal = Color.FromArgb(0, 190, 255);
    internal static readonly Color Warning = Color.FromArgb(255, 150, 0);
    internal static readonly Color Exceeded = Color.FromArgb(235, 40, 40);
    internal static readonly Color PausedColor = Color.FromArgb(150, 150, 150);

    internal static readonly Style Default = new(2f, 6f, Track, Normal);

    internal static void Paint(Graphics graphics, int size, bool paused, bool available, float fraction) =>
        Paint(graphics, size, paused, available, fraction, Default);

    internal static void Paint(Graphics graphics, int size, bool paused, bool available, float fraction, Style style)
    {
        var scale = size / Design;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(Color.Transparent);
        var half = Math.Max(style.TrackWidth, style.ProgressWidth) * scale / 2f;
        var ring = new RectangleF(half, half, size - 2 * half, size - 2 * half);
        using var track = new Pen(style.TrackColor, style.TrackWidth * scale);
        graphics.DrawEllipse(track, ring);
        fraction = Math.Clamp(fraction, 0f, 1f);
        if (available)
        {
            var color = paused ? PausedColor : fraction >= 1f ? Exceeded : fraction >= 0.8f ? Warning : style.ProgressColor;
            using var progress = new Pen(color, style.ProgressWidth * scale);
            if (fraction >= 1f) graphics.DrawEllipse(progress, ring);
            else if (fraction > 0f) graphics.DrawArc(progress, ring, -90, Math.Max(fraction * 360f, MinimumVisibleSweep));
        }
        else if (!paused)
        {
            using var slash = new Pen(style.TrackColor, 3f * scale);
            graphics.DrawLine(slash, 7f * scale, 25f * scale, 25f * scale, 7f * scale);
        }

        if (paused)
        {
            graphics.FillRectangle(Brushes.White, 11f * scale, 10f * scale, 4f * scale, 12f * scale);
            graphics.FillRectangle(Brushes.White, 17f * scale, 10f * scale, 4f * scale, 12f * scale);
        }
    }
}
