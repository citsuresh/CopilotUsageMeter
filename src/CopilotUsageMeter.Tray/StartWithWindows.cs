using System;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace CopilotUsageMeter.Tray;

// "Start with Windows" is a per-user Run value (no elevation, no scheduled task). The value is the quoted path of the running exe.
[SupportedOSPlatform("windows")]
internal static class StartWithWindows
{
    internal const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    internal const string ValueName = "CopilotUsageMeter";

    internal static bool IsEnabled(string keyPath = RunKeyPath)
    {
        using var key = Registry.CurrentUser.OpenSubKey(keyPath);
        return key?.GetValue(ValueName) is string value && value.Length > 0;
    }

    internal static void Set(bool enabled, string? exePath, string keyPath = RunKeyPath)
    {
        if (enabled)
        {
            if (string.IsNullOrWhiteSpace(exePath)) throw new InvalidOperationException("The tray executable path is unavailable.");
            using var key = Registry.CurrentUser.CreateSubKey(keyPath);
            key.SetValue(ValueName, Quote(exePath), RegistryValueKind.String);
        }
        else
        {
            using var key = Registry.CurrentUser.OpenSubKey(keyPath, writable: true);
            key?.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }

    internal static string Quote(string path) => "\"" + path.Trim('"') + "\"";
}
