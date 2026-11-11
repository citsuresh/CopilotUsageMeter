using System.Reflection;

namespace CopilotUsageMeter.Panel.Core
{
    /// <summary>Shows the product version of an assembly as "major.minor.patch", without any build metadata.</summary>
    public static class ProductVersion
    {
        public static string Of(Assembly assembly)
        {
            var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            return Format(informational ?? assembly.GetName().Version?.ToString());
        }

        public static string Format(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "";
            var text = raw!.Trim();
            var plus = text.IndexOf('+');
            if (plus >= 0) text = text.Substring(0, plus);
            var dash = text.IndexOf('-');
            if (dash >= 0) text = text.Substring(0, dash);
            var parts = text.Split('.');
            return parts.Length == 4 && parts[3] == "0" ? string.Join(".", parts, 0, 3) : text;
        }
    }
}
