using System;
using System.IO;
using System.Linq;
using System.Security.Principal;
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
    private FileDiagnosticsSink? log;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        log = new FileDiagnosticsSink(FileDiagnosticsSink.DefaultDirectory());
        Diagnostics.Sink = log;
        InstallGlobalHandlers();
        try
        {
            await StartAsync(e);
        }
        catch (Exception exception)
        {
            Diagnostics.Error("tray.startup", "startup_failed", exception);
            MessageBox.Show("Copilot Usage Meter could not start. Details were written to " + log.CurrentPath,
                "Copilot Usage Meter", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    // Keeps an unexpected fault in one handler from ending the tray: faults are logged and the user is told once.
    private void InstallGlobalHandlers()
    {
        DispatcherUnhandledException += (_, args) =>
        {
            Diagnostics.Error("tray.ui", "dispatcher_unhandled", args.Exception);
            args.Handled = true;
            host?.ReportUnexpectedFailure();
        };
        // Tray-icon and menu handlers run in WinForms' window procedure, which would otherwise show its own error dialog with the exception text.
        System.Windows.Forms.Application.SetUnhandledExceptionMode(System.Windows.Forms.UnhandledExceptionMode.CatchException);
        System.Windows.Forms.Application.ThreadException += (_, args) =>
        {
            Diagnostics.Error("tray.icon", "winforms_unhandled", args.Exception);
            host?.ReportUnexpectedFailure();
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Diagnostics.Error("tray.tasks", "task_unobserved", args.Exception);
            args.SetObserved();
            host?.ReportUnexpectedFailure();
        };
        // The runtime ends the process after this event; the entry is written synchronously so the cause survives.
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Diagnostics.Error("tray.process", args.IsTerminating ? "fatal_unhandled" : "unhandled", args.ExceptionObject as Exception);
    }

    private async Task StartAsync(StartupEventArgs e)
    {
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
                Diagnostics.Warning("tray.startup", "open_status_failed", exception);
                MessageBox.Show("The tray is already running but could not open its status window. Use its notification-area icon.", "Copilot Usage Meter");
            }
            Shutdown();
            return;
        }
        try
        {
            host = new TrayHost(user, ExitAsync);
            host.Start();
            if (e.Args.Contains("--show-status", StringComparer.Ordinal)) host.ShowWindow();
        }
        catch (Exception exception)
        {
            Diagnostics.Error("tray.startup", "tracking_start_failed", exception);
            MessageBox.Show("Unable to start tracking. Check access to the named pipe and local history directory. Details were written to " + log?.CurrentPath,
                "Copilot Usage Meter", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private async Task ExitAsync()
    {
        try { if (host != null) await host.StopAsync(); }
        catch (Exception exception) { Diagnostics.Error("tray.exit", "stop_failed", exception); }
        finally { Shutdown(); }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { host?.Dispose(); }
        catch (Exception exception) { Diagnostics.Error("tray.exit", "dispose_failed", exception); }
        try { if (ownsMutex) instance?.ReleaseMutex(); }
        catch (ApplicationException exception) { Diagnostics.Warning("tray.exit", "mutex_release_failed", exception); }
        instance?.Dispose();
        base.OnExit(e);
    }
}
