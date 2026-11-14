using CopilotUsageMeter.Core;
using CopilotUsageMeter.Tray;

namespace CopilotUsageMeter.Architecture.Tests;

public sealed class AppSettingsTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "cum-settings-" + Guid.NewGuid().ToString("N"));

    public AppSettingsTests() => Directory.CreateDirectory(folder);

    public void Dispose() => Directory.Delete(folder, recursive: true);

    private string FilePath => Path.Combine(folder, "settings.json");

    private AppSettingsStore Loaded()
    {
        var store = new AppSettingsStore(FilePath);
        store.Load();
        return store;
    }

    [Fact]
    public void DefaultsMatchTheDetailsWindowsOwnDefaults()
    {
        var settings = Loaded().Current;

        Assert.Equal(DetailsRange.Last30Days, settings.DefaultRange);
        Assert.Equal(DetailsGrouping.Day, settings.DefaultGrouping);
        Assert.False(settings.PauseUpdates);
        Assert.Null(settings.DetailsPlacement);
        Assert.Null(settings.LastExportFolder);
    }

    [Fact]
    public void SavedValuesSurviveAReload()
    {
        var export = Path.Combine(folder, "exports");
        var store = Loaded();
        Assert.True(store.Update(s =>
        {
            s.PauseUpdates = true;
            s.DefaultRange = DetailsRange.LastMonth;
            s.GroupByRepository = true;
            s.GroupByDay = false;
            s.DetailsPlacement = new WindowPlacement(10, 20, 700, 600);
            s.LastExportFolder = export;
        }));

        var reloaded = Loaded().Current;

        Assert.True(reloaded.PauseUpdates);
        Assert.Equal(DetailsRange.LastMonth, reloaded.DefaultRange);
        Assert.Equal(DetailsGrouping.Repository, reloaded.DefaultGrouping);
        Assert.Equal(new WindowPlacement(10, 20, 700, 600), reloaded.DetailsPlacement);
        Assert.Equal(export, reloaded.LastExportFolder);
    }

    [Fact]
    public void TheSavedFileHoldsOnlyTheKnownSettings()
    {
        var store = Loaded();
        store.Update(s =>
        {
            s.DetailsPlacement = new WindowPlacement(1, 2, 300, 400);
            s.LastExportFolder = folder;
        });

        using var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(FilePath));
        var names = json.RootElement.EnumerateObject().Select(property => property.Name).Order().ToArray();

        // No filter text, repository, model, account or chat field can reach the file without this list changing.
        Assert.Equal(new[]
        {
            "DefaultRange", "DetailsHeight", "DetailsLeft", "DetailsTop", "DetailsWidth", "GroupByDay", "GroupByModel",
            "GroupByRepository", "LastExportFolder", "PauseUpdates", "Version"
        }, names);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("{\"Version\":2,\"PauseUpdates\":true}")]
    [InlineData("{\"Version\":1,\"DefaultRange\":\"x\"}")]
    public void AnUnusableFileGivesDefaults(string content)
    {
        File.WriteAllText(FilePath, content);

        var settings = Loaded().Current;

        Assert.False(settings.PauseUpdates);
        Assert.Equal(DetailsRange.Last30Days, settings.DefaultRange);
    }

    [Fact]
    public void AnOversizedFileGivesDefaults()
    {
        File.WriteAllText(FilePath, "{\"Version\":1,\"PauseUpdates\":true,\"LastExportFolder\":\"" + new string('a', AlertFile.MaximumBytes) + "\"}");

        Assert.False(Loaded().Current.PauseUpdates);
    }

    [Theory]
    [InlineData(99)]
    [InlineData(-1)]
    [InlineData((int)DetailsRange.Custom)]
    public void ARangeThatIsNotAFixedRangeFallsBack(int range)
    {
        File.WriteAllText(FilePath, "{\"Version\":1,\"DefaultRange\":" + range + ",\"PauseUpdates\":true}");

        var settings = Loaded().Current;

        Assert.Equal(DetailsRange.Last30Days, settings.DefaultRange);
        Assert.True(settings.PauseUpdates);
    }

    [Fact]
    public void NoGroupingFallsBackToDay()
    {
        File.WriteAllText(FilePath, "{\"Version\":1,\"GroupByDay\":false,\"GroupByRepository\":false,\"GroupByModel\":false}");

        Assert.Equal(DetailsGrouping.Day, Loaded().Current.DefaultGrouping);
    }

    [Theory]
    [InlineData(10, 20, 0, 600)]
    [InlineData(10, 20, 700, -5)]
    [InlineData(1e9, 20, 700, 600)]
    [InlineData(10, 20, 1e9, 600)]
    public void AnUnusablePlacementIsForgotten(double left, double top, double width, double height)
    {
        var store = Loaded();

        store.Update(s => s.DetailsPlacement = new WindowPlacement(left, top, width, height));

        Assert.Null(store.Current.DetailsPlacement);
    }

    [Fact]
    public void ANegativeCoordinateOnAnotherMonitorIsKept()
    {
        var store = Loaded();

        store.Update(s => s.DetailsPlacement = new WindowPlacement(-1900, 40, 700, 600));

        Assert.Equal(new WindowPlacement(-1900, 40, 700, 600), store.Current.DetailsPlacement);
    }

    [Theory]
    [InlineData("relative\\folder")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("C:\\bad\0path")]
    [InlineData("C:\\bad\npath")]
    public void AFolderThatIsNotAFullPathIsForgotten(string value)
    {
        var store = Loaded();

        store.Update(s => s.LastExportFolder = value);

        Assert.Null(store.Current.LastExportFolder);
    }

    [Fact]
    public void ATooLongFolderIsForgotten()
    {
        var store = Loaded();

        store.Update(s => s.LastExportFolder = "C:\\" + new string('a', 300));

        Assert.Null(store.Current.LastExportFolder);
    }

    [Fact]
    public void AFailedSaveReportsFalseAndKeepsTheOldValues()
    {
        var store = Loaded();
        store.Update(s => s.DefaultRange = DetailsRange.Last7Days);

        // A directory in the way of the file makes the save fail.
        File.Delete(FilePath);
        Directory.CreateDirectory(FilePath);

        Assert.False(store.Update(s => s.DefaultRange = DetailsRange.Last90Days));
        Assert.Equal(DetailsRange.Last7Days, store.Current.DefaultRange);
        Assert.Empty(Directory.GetFiles(folder, "*.tmp"));
    }

    [Fact]
    public void CurrentIsACopyThatCannotChangeTheStore()
    {
        var store = Loaded();

        store.Current.PauseUpdates = true;

        Assert.False(store.Current.PauseUpdates);
    }

    [Fact]
    public void ForgettingPlacementAndFolderLeavesPauseAndDefaultsAlone()
    {
        var store = Loaded();
        store.Update(s =>
        {
            s.PauseUpdates = true;
            s.DefaultRange = DetailsRange.ThisMonth;
            s.DetailsPlacement = new WindowPlacement(1, 2, 300, 400);
            s.LastExportFolder = folder;
        });

        store.Update(s =>
        {
            s.DetailsPlacement = null;
            s.LastExportFolder = null;
        });

        var current = store.Current;
        Assert.Null(current.DetailsPlacement);
        Assert.Null(current.LastExportFolder);
        Assert.True(current.PauseUpdates);
        Assert.Equal(DetailsRange.ThisMonth, current.DefaultRange);
        Assert.Null(Loaded().Current.DetailsPlacement);
    }

    [Fact]
    public void ConcurrentUpdatesNeverLoseTheFileOrCorruptIt()
    {
        var store = Loaded();

        Parallel.For(0, 40, i => store.Update(s => s.PauseUpdates = i % 2 == 0));

        var onDisk = Loaded().Current;
        Assert.Equal(store.Current.PauseUpdates, onDisk.PauseUpdates);
    }

    [Fact]
    public void TheScheduleTextStatesEveryFixedInterval()
    {
        var text = RefreshScheduleText.Describe();

        Assert.Contains("after a 1.5 s pause", text);
        Assert.Contains("at most once every 5 s", text);
        Assert.Contains("every 5 min while chats are active", text);
        Assert.Contains("every 15 min otherwise", text);
    }
}
