using System;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Shell;

namespace CopilotUsageMeter.Panel.Vsix
{
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [Guid(PackageGuid)]
    public sealed class UsagePackage : AsyncPackage
    {
        public const string PackageGuid = "0c23c94b-6817-4e31-bdb9-6fb5296ed705";
    }
}