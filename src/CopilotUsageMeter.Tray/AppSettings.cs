using System;
using System.Globalization;
using System.IO;
using System.Text.Json.Serialization;
using CopilotUsageMeter.Contracts;
using CopilotUsageMeter.Core;

namespace CopilotUsageMeter.Tray;

// The tray's own preferences, kept in %LOCALAPPDATA%\CopilotUsageMeter\settings.json. They are local to this machine and never
// uploaded or logged. Start with Windows is not stored here (the Run value is its source of truth) and alert settings keep their own file.
internal sealed class AppSettings
{
    internal const int CurrentVersion = 1;
    private const double MaximumCoordinate = 100000;
    private const int MaximumFolderLength = 248;

    public int Version { get; set; } = CurrentVersion;

    // Remembered across tray restarts; automatic refreshes stay off until the user resumes.
    public bool PauseUpdates { get; set; }

    public DetailsRange DefaultRange { get; set; } = DetailsRange.Last30Days;
    public bool GroupByDay { get; set; } = true;
    public bool GroupByRepository { get; set; }
    public bool GroupByModel { get; set; }

    public double? DetailsLeft { get; set; }
    public double? DetailsTop { get; set; }
    public double? DetailsWidth { get; set; }
    public double? DetailsHeight { get; set; }

    public string? LastExportFolder { get; set; }

    [JsonIgnore]
    public DetailsGrouping DefaultGrouping =>
        (GroupByDay ? DetailsGrouping.Day : DetailsGrouping.None)
        | (GroupByRepository ? DetailsGrouping.Repository : DetailsGrouping.None)
        | (GroupByModel ? DetailsGrouping.Model : DetailsGrouping.None);

    [JsonIgnore]
    public WindowPlacement? DetailsPlacement
    {
        get => DetailsLeft.HasValue && DetailsTop.HasValue && DetailsWidth.HasValue && DetailsHeight.HasValue
            ? new WindowPlacement(DetailsLeft.Value, DetailsTop.Value, DetailsWidth.Value, DetailsHeight.Value)
            : null;
        set
        {
            DetailsLeft = value?.Left;
            DetailsTop = value?.Top;
            DetailsWidth = value?.Width;
            DetailsHeight = value?.Height;
        }
    }

    public AppSettings Clone() => (AppSettings)MemberwiseClone();

    // Anything out of range falls back to its default rather than failing the whole file.
    public AppSettings Normalized()
    {
        var copy = Clone();
        copy.Version = CurrentVersion;
        if (!Enum.IsDefined(typeof(DetailsRange), copy.DefaultRange) || copy.DefaultRange == DetailsRange.Custom) copy.DefaultRange = DetailsRange.Last30Days;
        if (!copy.GroupByDay && !copy.GroupByRepository && !copy.GroupByModel) copy.GroupByDay = true;

        if (copy.DetailsPlacement is not { IsUsable: true } placement || !IsSane(placement)) copy.DetailsPlacement = null;
        copy.LastExportFolder = NormalizeFolder(copy.LastExportFolder);
        return copy;
    }

    private static bool IsSane(WindowPlacement placement) =>
        Math.Abs(placement.Left) <= MaximumCoordinate && Math.Abs(placement.Top) <= MaximumCoordinate
        && placement.Width <= MaximumCoordinate && placement.Height <= MaximumCoordinate;

    private static string? NormalizeFolder(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder)) return null;
        folder = folder!.Trim();
        if (folder.Length > MaximumFolderLength || folder.IndexOfAny(Path.GetInvalidPathChars()) >= 0) return null;
        foreach (var character in folder) if (char.IsControl(character)) return null;
        try { return Path.IsPathFullyQualified(folder) ? folder : null; }
        catch (ArgumentException) { return null; }
    }
}

// The fixed refresh schedule as shown on the settings page, built from the scheduler constants so the text cannot drift from the behaviour.
internal static class RefreshScheduleText
{
    internal static string Describe()
    {
        return "Local usage refreshes when the history changes, after a " + Duration(LocalRefreshScheduler.Debounce) + " pause and at most once every "
            + Duration(LocalRefreshScheduler.Throttle) + ". GitHub account usage refreshes every " + Duration(AccountRefreshCoordinator.ActiveInterval)
            + " while chats are active and every " + Duration(AccountRefreshCoordinator.IdleInterval) + " otherwise, and on Refresh now. Pause updates stops the automatic refreshes.";
    }

    private static string Duration(TimeSpan value) =>
        value.TotalMinutes >= 1
            ? value.TotalMinutes.ToString("0.##", CultureInfo.InvariantCulture) + " min"
            : value.TotalSeconds.ToString("0.##", CultureInfo.InvariantCulture) + " s";
}

// Loads and saves the settings file. Reads never throw: a missing, oversized, corrupt or newer-version file gives defaults and a fixed
// diagnostic code. Saves are atomic; a failed save returns false and leaves both the file and the in-memory copy unchanged.
internal sealed class AppSettingsStore
{
    private readonly string path;
    private readonly object gate = new();
    private AppSettings current = new();

    public AppSettingsStore(string path) => this.path = path;

    public AppSettings Current
    {
        get { lock (gate) return current.Clone(); }
    }

    public void Load()
    {
        lock (gate)
        {
            var loaded = AlertFile.Load(path, "app_settings", () => new AppSettings());
            if (loaded.Version != AppSettings.CurrentVersion)
            {
                Diagnostics.Warning("app_settings", "unsupported-version");
                loaded = new AppSettings();
            }
            current = loaded.Normalized();
        }
    }

    public bool Update(Action<AppSettings> change)
    {
        lock (gate)
        {
            var next = current.Clone();
            change(next);
            next = next.Normalized();
            try { AlertFile.Save(path, next); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                Diagnostics.Warning("app_settings", "save-failed", exception);
                return false;
            }
            current = next;
            return true;
        }
    }
}
