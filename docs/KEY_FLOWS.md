# Key flows

- Tray startup: `App.OnStartup -> TrayHost.Start -> UsagePipeServer.Start`.
- Compact client: `UsagePackage.InitializeAsync -> ChatUsageIntegration() -> PipeUsageClient.Start -> CompactUsageView.Update -> CompactUsagePresentation.Create`.
- Active selection: `ChatUsageIntegration.Tick -> ReadActiveId -> ActiveChatIdentity.Read -> ReadMember -> ChatUsageIntegration.SendCommandAsync -> PipeUsageClient.SendAsync`.
- Local refresh: `LocalStoreChangeSignal.Changed -> TrayHost.DatabaseChanged -> LocalRefreshScheduler -> TrayHost.ReadLocalAsync -> ChatHistoryReader.ReadAsync / ReadCurrentChatAsync -> TrackingUsageState.UpdateLocal`.
- Per-context delivery: `TrayHost.RunPublicationsAsync -> UsagePipeServer.BroadcastAsync -> TrackingUsageState.ProjectContext -> PipeUsageClient -> CompactUsageView.Update`.
- Account refresh: `AccountRefreshCoordinator.RunAsync -> GitHubAccountReader.ReadAsync -> GitHubApiClient -> TrayHost.AccountChanged -> TrackingUsageState.UpdateAccount`.
- Tray Exit: `App.ExitAsync -> TrayHost.StopAsync -> UsagePipeServer.StopAsync -> TrayHost.Dispose`.
- GitHub sign-in: `TrayHost.StartBrowserSignIn -> GitHubSignInFlow.Begin -> GitHubCliSignIn.Begin -> GitHubCliProcessLauncher -> GitHubSignInWindow -> TrayHost.ConnectSignedInAccountAsync` (no code in time: `GitHubSignInFlow -> TrayHost.StartVisibleSignIn`).
- Remove saved account: `MainWindow.AskToRemoveAccount -> MainWindow.ConfirmRemoveAccountClicked -> GitHubAccountRemoval.RemoveAsync -> GitHubCliLogout.RemoveAsync` (connected login: `-> TrayHost.SignOutTrayAsync -> GitHubAccountReader.SignOutAsync`).
- Failure logging: `Diagnostics.Error -> FileDiagnosticsSink.Write` (tray, `logs\tray.log`) / `ActivityLogDiagnosticsSink.Write` (VSIX); tray unexpected fault: `App.InstallGlobalHandlers -> TrayHost.ReportUnexpectedFailure -> TrayHost.QueueFault -> MainWindow.SetFault`; worker fault: `TrayHost.SuperviseAsync -> Diagnostics.Error -> restart`.
