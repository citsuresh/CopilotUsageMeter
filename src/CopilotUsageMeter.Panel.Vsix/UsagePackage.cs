using System;
using System.ComponentModel.Design;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace CopilotUsageMeter.Panel.Vsix
{
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [ProvideMenuResource("Menus.ctmenu", 1)]
    [ProvideToolWindow(typeof(UsageDemoToolWindow))]
    [Guid(PackageGuid)]
    public sealed class UsagePackage : AsyncPackage
    {
        public const string PackageGuid = "0c23c94b-6817-4e31-bdb9-6fb5296ed705";
        private static readonly Guid CommandSet = new Guid("f975a94c-b23c-4239-a413-e3dc7275b87e");

        protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
        {
            await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
            var service = await GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService
                ?? throw new InvalidOperationException("Visual Studio menu service is unavailable.");
            service.AddCommand(new MenuCommand((_, _) => JoinableTaskFactory.RunAsync(async () =>
            {
                try
                {
                    var window = await ShowToolWindowAsync(typeof(UsageDemoToolWindow), 0, true, DisposalToken);
                    if (window == null) throw new InvalidOperationException("Unable to create the Phase 2 demo test window.");
                }
                catch (OperationCanceledException) when (DisposalToken.IsCancellationRequested) { }
                catch (Exception exception) when (exception is COMException || exception is InvalidOperationException)
                {
                    ActivityLog.LogError(nameof(UsagePackage), $"Demo window creation failed: {exception.GetType().Name}, HRESULT 0x{exception.HResult:X8}.");
                    VsShellUtilities.ShowMessageBox(this, "The demo window could not be opened. Check the Visual Studio ActivityLog and retry.",
                        "Copilot Usage Meter - demo", OLEMSGICON.OLEMSGICON_WARNING, OLEMSGBUTTON.OLEMSGBUTTON_OK, OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
                }
            }), new CommandID(CommandSet, 0x0100)));
            // Phase 3 probes touch Copilot's live visual tree and write to %TEMP%: register them only in the experimental instance.
            if (!IsExperimentalInstance()) return;
            service.AddCommand(new MenuCommand((_, _) =>
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                try { ChatPlacementProbe.Run(); }
                catch (Exception exception)
                {
                    ActivityLog.LogError(nameof(UsagePackage), $"Placement probe failed: {exception.GetType().Name}.");
                }
            }, new CommandID(CommandSet, 0x0101)));
            service.AddCommand(new MenuCommand((_, _) =>
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                try { ChatPlacementProbe.RunTopOfPane(); }
                catch (Exception exception)
                {
                    ActivityLog.LogError(nameof(UsagePackage), $"Top-of-pane probe failed: {exception.GetType().Name}.");
                }
            }, new CommandID(CommandSet, 0x0102)));
            service.AddCommand(new MenuCommand((_, _) =>
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                try { ActiveChatProbe.Run(); }
                catch (Exception exception)
                {
                    ActivityLog.LogError(nameof(UsagePackage), $"Active chat probe failed: {exception.GetType().Name}.");
                }
            }, new CommandID(CommandSet, 0x0103)));
        }

        private static bool IsExperimentalInstance()
        {
            var arguments = Environment.GetCommandLineArgs();
            for (var i = 0; i < arguments.Length - 1; i++)
            {
                if ((arguments[i].Equals("/rootsuffix", StringComparison.OrdinalIgnoreCase) || arguments[i].Equals("-rootsuffix", StringComparison.OrdinalIgnoreCase))
                    && arguments[i + 1].Equals("Exp", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}