using System.Windows;
using System.Windows.Media;
using CopilotUsageMeter.Panel.Core;

namespace CopilotUsageMeter.Presentation
{
    internal static class UsageTheme
    {
        public static bool HighContrast => SystemParameters.HighContrast;
        public static Brush Fill(Brush normal) => HighContrast ? SystemColors.HighlightBrush : normal;
        public static Brush Track(Brush normal) => HighContrast ? SystemColors.WindowBrush : normal;
        public static Brush Edge(Brush normal) => HighContrast ? SystemColors.WindowTextBrush : normal;
        public static double Opacity(double normal) => HighContrast ? 1 : normal;

        public static Brush TextOn(Brush background)
        {
            if (HighContrast) return SystemColors.HighlightTextBrush;
            if (!(background is SolidColorBrush solid) || solid.Color.A != 255)
                throw new System.ArgumentException("Badge backgrounds must be opaque solid colours.", nameof(background));
            var color = solid.Color;
            return ColorContrast.UseDarkText(color.R, color.G, color.B) ? Brushes.Black : Brushes.White;
        }
    }
}