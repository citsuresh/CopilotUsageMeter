using System;
using System.ComponentModel.Design;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using CopilotUsageMeter.Contracts;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace CopilotUsageMeter.Panel.Vsix
{
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [ProvideMenuResource("Menus.ctmenu", 1)]
    [ProvideAutoLoad(UIContextGuids80.NoSolution, PackageAutoLoadFlags.BackgroundLoad)]
    [ProvideAutoLoad(UIContextGuids80.SolutionExists, PackageAutoLoadFlags.BackgroundLoad)]
    [Guid(PackageGuid)]
    public sealed class UsagePackage : AsyncPackage
    {
        public const string PackageGuid = "0c23c94b-6817-4e31-bdb9-6fb5296ed705";
        private static readonly Guid CommandSet = new Guid("f975a94c-b23c-4239-a413-e3dc7275b87e");
        private ChatUsageIntegration? integration;

        protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
        {
            Diagnostics.Sink = new ActivityLogDiagnosticsSink();
            try
            {
                await InitializeCoreAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                // Failing here would show Visual Studio's package-load error; the extension logs and stays inactive instead.
                Diagnostics.Error("vsix.package", "initialize_failed", exception);
                await ShowStartupFailureAsync(cancellationToken);
            }
        }

        private async Task ShowStartupFailureAsync(CancellationToken cancellationToken)
        {
            try
            {
                await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
                if (await GetServiceAsync(typeof(SVsStatusbar)) is IVsStatusbar statusBar)
                    statusBar.SetText("Copilot Usage Meter could not start; details are in the Visual Studio activity log.");
            }
            catch (Exception)
            {
            }
        }

        private async Task InitializeCoreAsync(CancellationToken cancellationToken)
        {
            await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
            var service = await GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService
                ?? throw new InvalidOperationException("Visual Studio menu service is unavailable.");
            var statusBar = await GetServiceAsync(typeof(SVsStatusbar)) as IVsStatusbar
                ?? throw new InvalidOperationException("Visual Studio status bar is unavailable.");
            integration = new ChatUsageIntegration(this, statusBar, JoinableTaskFactory);
            service.AddCommand(new MenuCommand((_, _) => integration.OpenStatus(), new CommandID(CommandSet, 0x0100)));
            service.AddCommand(new MenuCommand((_, _) => integration.Recheck(), new CommandID(CommandSet, 0x0101)));
            service.AddCommand(new MenuCommand((_, _) => integration.Recheck(1), new CommandID(CommandSet, 0x0102)));
            service.AddCommand(new MenuCommand((_, _) => integration.Recheck(2), new CommandID(CommandSet, 0x0103)));
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && integration != null)
            {
                try
                {
                    JoinableTaskFactory.Run(async () =>
                    {
                        await JoinableTaskFactory.SwitchToMainThreadAsync();
                        integration.Dispose();
                        integration = null;
                    });
                }
                catch (Exception exception)
                {
                    Diagnostics.Error("vsix.package", "dispose_failed", exception);
                }
            }
            base.Dispose(disposing);
        }
    }
}