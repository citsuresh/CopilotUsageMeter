using System;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using CopilotUsageMeter.Client;
using CopilotUsageMeter.Contracts;

namespace CopilotUsageMeter.Tray;

public partial class App : Application
{
    private Mutex? instance;
    private bool ownsMutex;
    private TrayHost? host;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        using var identity = WindowsIdentity.GetCurrent();
        var user = identity.User ?? throw new InvalidOperationException("Windows user SID is unavailable.");
        instance = new Mutex(true, "Local\\CopilotUsageMeter." + user.Value, out ownsMutex);
        if (!ownsMutex)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var client = new PipeUsageClient(UsageProtocol.PipePrefix + user.Value);
            try
            {
                client.Start();
                while (client.Snapshot == null) await Task.Delay(50, timeout.Token);
                var response = await client.SendAsync(new ProtocolMessage { Type = "OpenStatus" }, timeout.Token);
                if (response.Type == "Error") throw new IOException("Existing tray rejected the status command.");
                await client.StopAsync();
            }
            catch (Exception exception) when (exception is IOException || exception is OperationCanceledException)
            {
                MessageBox.Show("The tray is already running but could not open its status window. Use its notification-area icon.", "Copilot Usage Meter");
            }
            Shutdown();
            return;
        }
        try
        {
            host = new TrayHost(user, new DemoSettingsStore(System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CopilotUsageMeter", "settings.json")), ExitAsync);
            host.Start();
            if (e.Args.Contains("--show-status", StringComparer.Ordinal)) host.ShowWindow();
        }
        catch (Exception exception) when (exception is InvalidDataException || exception is IOException || exception is UnauthorizedAccessException
            || exception is JsonException || exception is ArgumentOutOfRangeException)
        {
            MessageBox.Show("Unable to start the demo tray. Check access to the named pipe and the demo settings file under LocalAppData\\CopilotUsageMeter.",
                "Copilot Usage Meter", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private async Task ExitAsync()
    {
        if (host != null) await host.StopAsync();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        host?.Dispose();
        if (ownsMutex) instance?.ReleaseMutex();
        instance?.Dispose();
        base.OnExit(e);
    }
}
