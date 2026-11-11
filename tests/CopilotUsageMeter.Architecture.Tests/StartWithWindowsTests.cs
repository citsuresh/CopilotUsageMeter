using System.Runtime.Versioning;
using CopilotUsageMeter.Tray;
using Microsoft.Win32;

namespace CopilotUsageMeter.Architecture.Tests;

// Uses a throwaway HKCU subkey, never the real Run key.
[SupportedOSPlatform("windows")]
public sealed class StartWithWindowsTests : IDisposable
{
    private readonly string keyPath = @"Software\CopilotUsageMeterTests\Run-" + Guid.NewGuid().ToString("N");

    public void Dispose() => Registry.CurrentUser.DeleteSubKeyTree(@"Software\CopilotUsageMeterTests", throwOnMissingSubKey: false);

    [Fact]
    public void Is_disabled_when_the_key_or_value_is_missing()
    {
        Assert.False(StartWithWindows.IsEnabled(keyPath));
    }

    [Fact]
    public void Enabling_writes_the_quoted_path_and_disabling_removes_it()
    {
        StartWithWindows.Set(true, @"C:\Program Files\Copilot Usage Meter\CopilotUsageMeter.Tray.exe", keyPath);

        Assert.True(StartWithWindows.IsEnabled(keyPath));
        using (var key = Registry.CurrentUser.OpenSubKey(keyPath))
            Assert.Equal("\"C:\\Program Files\\Copilot Usage Meter\\CopilotUsageMeter.Tray.exe\"", key!.GetValue(StartWithWindows.ValueName));

        StartWithWindows.Set(false, null, keyPath);
        Assert.False(StartWithWindows.IsEnabled(keyPath));
    }

    [Fact]
    public void Enabling_twice_keeps_a_single_value()
    {
        StartWithWindows.Set(true, @"C:\a\tray.exe", keyPath);
        StartWithWindows.Set(true, @"C:\b\tray.exe", keyPath);

        using var key = Registry.CurrentUser.OpenSubKey(keyPath);
        Assert.Single(key!.GetValueNames());
        Assert.Equal("\"C:\\b\\tray.exe\"", key.GetValue(StartWithWindows.ValueName));
    }

    [Fact]
    public void Disabling_when_nothing_is_set_does_not_throw()
    {
        StartWithWindows.Set(false, null, keyPath);
        Assert.False(StartWithWindows.IsEnabled(keyPath));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Enabling_without_a_path_is_rejected(string? path)
    {
        Assert.Throws<InvalidOperationException>(() => StartWithWindows.Set(true, path, keyPath));
        Assert.False(StartWithWindows.IsEnabled(keyPath));
    }

    [Fact]
    public void Quote_does_not_double_quote()
    {
        Assert.Equal("\"C:\\x\\t.exe\"", StartWithWindows.Quote("\"C:\\x\\t.exe\""));
    }
}
