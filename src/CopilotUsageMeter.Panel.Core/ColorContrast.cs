using System;

namespace CopilotUsageMeter.Panel.Core
{
    public static class ColorContrast
    {
        public static double Luminance(byte red, byte green, byte blue) =>
            0.2126 * Linear(red) + 0.7152 * Linear(green) + 0.0722 * Linear(blue);

        public static bool UseDarkText(byte red, byte green, byte blue)
        {
            var luminance = Luminance(red, green, blue);
            return (luminance + 0.05) / 0.05 >= 1.05 / (luminance + 0.05);
        }

        private static double Linear(byte value)
        {
            var channel = value / 255.0;
            return channel <= 0.04045 ? channel / 12.92 : Math.Pow((channel + 0.055) / 1.055, 2.4);
        }
    }
}