# Key flows

- Tray startup: `App.OnStartup -> TrayHost.Start -> UsagePipeServer.Start`.
- Compact client: `UsagePackage.InitializeAsync -> ChatUsageIntegration() -> PipeUsageClient.Start -> CompactUsageView.Update -> CompactUsagePresentation.Create`.
- Active selection: `ChatUsageIntegration.Tick -> ReadActiveId -> ActiveChatIdentity.Read -> ReadMember -> ChatUsageIntegration.SendCommandAsync -> PipeUsageClient.SendAsync`.
- Local refresh: `LocalStoreChangeSignal.Changed -> TrayHost.DatabaseChanged -> LocalRefreshScheduler -> TrayHost.ReadLocalAsync -> ChatHistoryReader.ReadAsync / ReadCurrentChatAsync -> TrackingUsageState.UpdateLocal`.
- Per-context delivery: `TrayHost.RunPublicationsAsync -> UsagePipeServer.BroadcastAsync -> TrackingUsageState.ProjectContext -> PipeUsageClient -> CompactUsageView.Update`.
- Account refresh: `AccountRefreshCoordinator.RunAsync -> GitHubAccountReader.ReadAsync -> GitHubApiClient -> TrayHost.AccountChanged -> TrackingUsageState.UpdateAccount`.
- Tray Exit: `App.ExitAsync -> TrayHost.StopAsync -> UsagePipeServer.StopAsync -> TrayHost.Dispose`.
