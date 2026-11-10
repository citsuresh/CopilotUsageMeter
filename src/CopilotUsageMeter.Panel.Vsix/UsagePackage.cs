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
            await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
            var service = await GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService
                ?? throw new InvalidOperationException("Visual Studio menu service is unavailable.");
            if (!IsExperimentalInstance()) return;
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
                JoinableTaskFactory.Run(async () =>
                {
                    await JoinableTaskFactory.SwitchToMainThreadAsync();
                    integration.Dispose();
                    integration = null;
                });
            base.Dispose(disposing);
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