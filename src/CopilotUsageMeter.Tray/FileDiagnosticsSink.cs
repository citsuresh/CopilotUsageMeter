using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using CopilotUsageMeter.Contracts;

namespace CopilotUsageMeter.Tray;

// Appends privacy-safe diagnostic lines to a small local log that rolls over at a size limit. It is never uploaded.
internal sealed class FileDiagnosticsSink : IDiagnosticsSink
{
    public const long DefaultMaxBytes = 256 * 1024;
    private static readonly TimeSpan RepeatWindow = TimeSpan.FromSeconds(10);
    private readonly object gate = new();
    private readonly Dictionary<string, DateTimeOffset> recent = new(StringComparer.Ordinal);
    private readonly Func<DateTimeOffset> clock;
    private readonly long maxBytes;

    public FileDiagnosticsSink(string directory, long maxBytes = DefaultMaxBytes, Func<DateTimeOffset>? clock = null)
    {
        Directory = directory;
        this.maxBytes = maxBytes;
        this.clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public string Directory { get; }

    public string CurrentPath => Path.Combine(Directory, "tray.log");

    public static string DefaultDirectory() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CopilotUsageMeter", "logs");

    public void Write(DiagnosticLevel level, string component, string code, string? exceptionDetail)
    {
        try
        {
            var now = clock();
            var key = level + "|" + component + "|" + code + "|" + exceptionDetail;
            var line = new StringBuilder()
                .Append(now.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture)).Append(' ')
                .Append(level == DiagnosticLevel.Error ? "ERROR" : "WARN").Append(' ')
                .Append(component).Append(' ').Append(code);
            if (exceptionDetail != null) line.Append(' ').Append(exceptionDetail);
            line.AppendLine();
            lock (gate)
            {
                // An identical failure repeating in a tight loop is recorded once per window so it cannot flood the file.
                if (recent.TryGetValue(key, out var last) && now - last < RepeatWindow) return;
                if (recent.Count > 256) recent.Clear();
                recent[key] = now;
                System.IO.Directory.CreateDirectory(Directory);
                Roll();
                File.AppendAllText(CurrentPath, line.ToString(), Encoding.UTF8);
            }
        }
        catch (Exception)
        {
            // Logging is best effort: a full disk or locked file must never become a new failure.
        }
    }

    private void Roll()
    {
        var current = new FileInfo(CurrentPath);
        if (!current.Exists || current.Length < maxBytes) return;
        var previous = Path.Combine(Directory, "tray.1.log");
        if (File.Exists(previous)) File.Delete(previous);
        File.Move(CurrentPath, previous);
    }
}
