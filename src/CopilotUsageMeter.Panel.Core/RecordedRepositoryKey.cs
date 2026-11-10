using System;
using System.Text.RegularExpressions;

namespace CopilotUsageMeter.Panel.Core
{
    public static class RecordedRepositoryKey
    {
        public static string? FromOrigin(string? origin)
        {
            if (string.IsNullOrWhiteSpace(origin)) return null;
            var value = origin!.Trim();
            string path;
            if (value.StartsWith("git@github.com:", StringComparison.OrdinalIgnoreCase)) path = value.Substring(15);
            else if (Uri.TryCreate(value, UriKind.Absolute, out var uri)
                && (uri.Scheme == "https" || uri.Scheme == "ssh")
                && uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
                && uri.Query.Length == 0 && uri.Fragment.Length == 0)
                path = uri.AbsolutePath.TrimStart('/');
            else return null;
            path = path.TrimEnd('/');
            if (path.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) path = path.Substring(0, path.Length - 4);
            return Regex.IsMatch(path, @"\A[A-Za-z0-9-]{1,39}/[A-Za-z0-9._-]{1,100}\z")
                && !path.EndsWith("/.", StringComparison.Ordinal) && !path.EndsWith("/..", StringComparison.Ordinal)
                ? path : null;
        }
    }
}