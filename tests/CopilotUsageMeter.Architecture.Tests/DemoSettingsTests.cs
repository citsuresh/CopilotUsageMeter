using CopilotUsageMeter.Tray;

namespace CopilotUsageMeter.Architecture.Tests;

public sealed class DemoSettingsTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "CopilotUsageMeter.Tests." + Guid.NewGuid().ToString("N"));
    private string SettingsPath => Path.Combine(directory, "settings.json");

    [Theory]
    [InlineData("null", typeof(InvalidDataException))]
    [InlineData("{broken}", typeof(System.Text.Json.JsonException))]
    [InlineData("{\"DemoRefreshSeconds\":4}", typeof(ArgumentOutOfRangeException))]
    [InlineData("{\"DemoRefreshSeconds\":301}", typeof(ArgumentOutOfRangeException))]
    public void InvalidPersistedSettingsAreReportedRatherThanReplaced(string json, Type exceptionType)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(SettingsPath, json);
        var exception = Record.Exception(() => new DemoSettingsStore(SettingsPath).Load());
        Assert.IsType(exceptionType, exception);
        Assert.Equal(json, File.ReadAllText(SettingsPath));
    }

    [Fact]
    public void OversizedSettingsAreExplicitlyRejected()
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(SettingsPath, new string('0', 4097));
        Assert.Throws<InvalidDataException>(() => new DemoSettingsStore(SettingsPath).Load());
    }

    [Theory]
    [InlineData(5)]
    [InlineData(300)]
    public void SettingsPersistAndInvalidSaveLeavesExistingValueUnchanged(int interval)
    {
        var store = new DemoSettingsStore(SettingsPath);
        Assert.Equal(15, store.Load().DemoRefreshSeconds);
        store.Save(interval);
        Assert.Equal(interval, new DemoSettingsStore(SettingsPath).Load().DemoRefreshSeconds);
        Assert.Throws<ArgumentOutOfRangeException>(() => store.Save(4));
        Assert.Equal(interval, store.Load().DemoRefreshSeconds);
        Assert.False(File.Exists(SettingsPath + ".tmp"));
    }

    public void Dispose()
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
}