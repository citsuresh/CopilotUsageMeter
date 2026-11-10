# Key flows

- Tray startup: `App.OnStartup -> TrayHost.Start -> UsagePipeServer.Start`.
- Demo client: `UsagePackage.InitializeAsync -> UsageDemoToolWindow() -> UsagePanelViewModel.Start -> PipeUsageClient.Start`.
- Shared demo refresh: `TrayHost.ProcessAsync -> DemoUsageEngine.Refresh -> TrayHost.PublishAsync -> UsagePipeServer.BroadcastAsync`.
- Tray Exit: `App.ExitAsync -> TrayHost.StopAsync -> UsagePipeServer.StopAsync -> TrayHost.Dispose`.
