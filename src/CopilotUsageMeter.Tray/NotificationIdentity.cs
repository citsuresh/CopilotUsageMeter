using System;
using System.IO;
using System.Runtime.InteropServices;
using CopilotUsageMeter.Contracts;
using Microsoft.Win32;

namespace CopilotUsageMeter.Tray;

// Windows attributes a notification to an app by its AppUserModelID. Without one it shows the exe name and a blank placeholder glyph, so the tray
// declares an identity with a display name and the ring icon. It is written under the current user only (no elevation) and failure is never fatal.
internal static class NotificationIdentity
{
    internal const string AppId = "CopilotUsageMeter.Tray";
    private const string DisplayName = "Copilot Usage Meter";
    private const string IconFile = "app.ico";

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);

    internal static void Register()
    {
        try
        {
            SetCurrentProcessExplicitAppUserModelID(AppId);
            using var key = Registry.CurrentUser.CreateSubKey(@"Software\Classes\AppUserModelId\" + AppId);
            if (key == null) return;
            key.SetValue("DisplayName", DisplayName, RegistryValueKind.String);
            var icon = Path.Combine(AppContext.BaseDirectory, IconFile);
            if (File.Exists(icon)) key.SetValue("IconUri", icon, RegistryValueKind.String);
        }
        catch (Exception exception)
        {
            Diagnostics.Warning("tray.startup", "notification_identity_failed", exception);
        }
    }
}
