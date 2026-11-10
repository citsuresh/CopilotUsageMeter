using System;
using System.IO;
using System.Text.Json;
using CopilotUsageMeter.Contracts;
using CopilotUsageMeter.Core;

namespace CopilotUsageMeter.Tray;

internal sealed class DemoSettingsStore
{
    private readonly string path;

    public DemoSettingsStore(string path) => this.path = path;

    public UsageSettings Load()
    {
        if (!File.Exists(path)) return new UsageSettings();
        if (new FileInfo(path).Length > 4096) throw new InvalidDataException("Settings file is too large.");
        var settings = JsonSerializer.Deserialize<UsageSettings>(File.ReadAllText(path))
            ?? throw new InvalidDataException("Settings file does not contain an object.");
        DemoUsageEngine.ValidateInterval(settings.DemoRefreshSeconds);
        return settings;
    }

    public void Save(int interval)
    {
        DemoUsageEngine.ValidateInterval(interval);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(new UsageSettings { DemoRefreshSeconds = interval }));
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}