using System;
using System.IO;
using System.Text.Json;
using CopilotUsageMeter.Contracts;
using CopilotUsageMeter.Core;

namespace CopilotUsageMeter.Tray;

// Small JSON files under %LOCALAPPDATA%\CopilotUsageMeter. A missing, oversized or corrupt file is never fatal: the caller gets
// defaults and a fixed diagnostic code is logged (never file content).
internal static class AlertFile
{
    internal const int MaximumBytes = 8192;

    internal static T Load<T>(string path, string component, Func<T> defaults) where T : class
    {
        try
        {
            if (!File.Exists(path)) return defaults();
            if (new FileInfo(path).Length > MaximumBytes) throw new InvalidDataException("File is too large.");
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path)) ?? throw new InvalidDataException("File does not contain an object.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or InvalidDataException or NotSupportedException)
        {
            Diagnostics.Warning(component, "load-failed", exception);
            return defaults();
        }
    }

    internal static void Save<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(value));
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}

internal sealed class AlertSettingsStore
{
    private readonly string path;

    public AlertSettingsStore(string path) => this.path = path;

    public AlertSettings Load() => AlertFile.Load(path, "alert_settings", () => new AlertSettings()).Normalized();

    public void Save(AlertSettings settings) => AlertFile.Save(path, settings.Normalized());
}

internal sealed class AlertStateStore
{
    private readonly string path;

    public AlertStateStore(string path) => this.path = path;

    public ThresholdAlertState Load() => AlertFile.Load(path, "alert_state", () => new ThresholdAlertState());

    public void Save(ThresholdAlertState state) => AlertFile.Save(path, state);
}
