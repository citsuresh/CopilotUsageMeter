using System;
using CopilotUsageMeter.Contracts;
using Microsoft.VisualStudio.Shell;

namespace CopilotUsageMeter.Panel.Vsix
{
    // Writes privacy-safe entries to Visual Studio's activity log (devenv /log, ActivityLog.xml).
    internal sealed class ActivityLogDiagnosticsSink : IDiagnosticsSink
    {
        private const string Source = "CopilotUsageMeter";

        public void Write(DiagnosticLevel level, string component, string code, string? exceptionDetail)
        {
            var text = component + " " + code + (exceptionDetail == null ? "" : " " + exceptionDetail);
            if (level == DiagnosticLevel.Error) ActivityLog.LogError(Source, text);
            else ActivityLog.LogWarning(Source, text);
        }
    }
}
