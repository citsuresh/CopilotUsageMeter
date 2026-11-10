using System;
using System.IO;
using CopilotUsageMeter.Panel.Core;

namespace CopilotUsageMeter.Panel.Vsix
{
    internal static class RepositoryContextReader
    {
        public static string? Read(string? solutionFile)
        {
            if (string.IsNullOrEmpty(solutionFile)) return null;
            try
            {
                for (var directory = new DirectoryInfo(Path.GetDirectoryName(solutionFile)); directory != null; directory = directory.Parent)
                {
                    var git = Path.Combine(directory.FullName, ".git");
                    if (File.Exists(git))
                    {
                        var pointer = File.ReadAllText(git).Trim();
                        if (!pointer.StartsWith("gitdir: ", StringComparison.Ordinal)) return null;
                        git = Path.GetFullPath(Path.Combine(directory.FullName, pointer.Substring(8)));
                        var common = Path.Combine(git, "commondir");
                        if (File.Exists(common)) git = Path.GetFullPath(Path.Combine(git, File.ReadAllText(common).Trim()));
                    }
                    if (!Directory.Exists(git)) continue;
                    var config = Path.Combine(git, "config");
                    if (!File.Exists(config)) return null;
                    var origin = false;
                    foreach (var line in File.ReadLines(config))
                    {
                        var trimmed = line.Trim();
                        if (trimmed.StartsWith("[", StringComparison.Ordinal)) origin = trimmed.Equals("[remote \"origin\"]", StringComparison.OrdinalIgnoreCase);
                        if (!origin) continue;
                        var equals = trimmed.IndexOf('=');
                        if (equals > 0 && trimmed.Substring(0, equals).Trim().Equals("url", StringComparison.OrdinalIgnoreCase))
                            return RecordedRepositoryKey.FromOrigin(trimmed.Substring(equals + 1).Trim().Trim('"'));
                    }
                    return null;
                }
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException || exception is ArgumentException || exception is NotSupportedException)
            {
                Microsoft.VisualStudio.Shell.ActivityLog.LogWarning(nameof(RepositoryContextReader), "Recorded repository context is unavailable.");
            }
            return null;
        }
    }
}