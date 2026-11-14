using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CopilotUsageMeter.Core
{
    [Flags]
    public enum DetailsGrouping
    {
        None = 0,
        Day = 1,
        Repository = 2,
        Model = 4
    }

    public interface IDetailsReader
    {
        Task<DetailsReport> ReadDetailsAsync(DetailsQuery query, CancellationToken cancellationToken);
    }

    // A validated, day-granular local-usage question. Filters are exact, case-insensitive matches and must name a dimension
    // that is also grouped, so every returned row states the value it was filtered by.
    public sealed class DetailsQuery
    {
        public const int MaximumDays = 366;
        public const int MaximumRows = 500;
        public const string UnsupportedOrigin = "(unsupported origin)";

        public DetailsQuery(DateTimeOffset fromUtc, DateTimeOffset toUtc, DetailsGrouping grouping,
            string? repositoryFilter = null, string? modelFilter = null)
        {
            var from = fromUtc.ToUniversalTime();
            var to = toUtc.ToUniversalTime();
            if (from.TimeOfDay != TimeSpan.Zero || to.TimeOfDay != TimeSpan.Zero) throw new ArgumentException("Bounds must be UTC midnights.");
            if (to <= from || to - from > TimeSpan.FromDays(MaximumDays)) throw new ArgumentOutOfRangeException(nameof(toUtc));
            if (grouping == DetailsGrouping.None || (grouping & ~(DetailsGrouping.Day | DetailsGrouping.Repository | DetailsGrouping.Model)) != 0)
                throw new ArgumentOutOfRangeException(nameof(grouping));
            if (repositoryFilter != null)
            {
                if (!IsValidRepositoryFilter(repositoryFilter)) throw new ArgumentException("Invalid repository filter.", nameof(repositoryFilter));
                if ((grouping & DetailsGrouping.Repository) == 0) throw new ArgumentException("A repository filter requires repository grouping.", nameof(grouping));
            }
            if (modelFilter != null)
            {
                if (!IsValidModelFilter(modelFilter)) throw new ArgumentException("Invalid model filter.", nameof(modelFilter));
                if ((grouping & DetailsGrouping.Model) == 0) throw new ArgumentException("A model filter requires model grouping.", nameof(grouping));
            }
            FromUtc = from;
            ToUtc = to;
            Grouping = grouping;
            RepositoryFilter = repositoryFilter;
            ModelFilter = modelFilter;
        }

        public DateTimeOffset FromUtc { get; }
        public DateTimeOffset ToUtc { get; }
        public DetailsGrouping Grouping { get; }
        public string? RepositoryFilter { get; }
        public string? ModelFilter { get; }

        // The history tool's group-by value, in its fixed day, repo, model order.
        public string GroupByArgument => GroupByText(Grouping);

        internal static string GroupByText(DetailsGrouping grouping)
        {
            var parts = new List<string>(3);
            if ((grouping & DetailsGrouping.Day) != 0) parts.Add("day");
            if ((grouping & DetailsGrouping.Repository) != 0) parts.Add("repo");
            if ((grouping & DetailsGrouping.Model) != 0) parts.Add("model");
            return string.Join(",", parts);
        }

        // owner/repo in GitHub's public naming; anything else cannot be used as a filter.
        public static bool IsValidRepositoryFilter(string? value)
        {
            if (value == null) return false;
            var parts = value.Split('/');
            if (parts.Length != 2 || parts[0].Length == 0 || parts[0].Length > 39 || parts[1].Length == 0 || parts[1].Length > 100
                || parts[0][0] == '-' || parts[1] == "." || parts[1] == "..") return false;
            foreach (var character in parts[0]) if (!(IsAsciiAlphaNumeric(character) || character == '-')) return false;
            foreach (var character in parts[1]) if (!(IsAsciiAlphaNumeric(character) || character == '-' || character == '_' || character == '.')) return false;
            return true;
        }

        // Must start with a letter or digit (never an option) and use no spaces or shell-significant characters.
        public static bool IsValidModelFilter(string? value)
        {
            if (string.IsNullOrEmpty(value) || value!.Length > 100 || !IsAsciiAlphaNumeric(value[0])) return false;
            foreach (var character in value)
                if (!(IsAsciiAlphaNumeric(character) || character == '-' || character == '_' || character == '.' || character == ':' || character == '/')) return false;
            return true;
        }

        public static bool IsValidGroupByArgument(string? value)
        {
            switch (value)
            {
                case "day": case "repo": case "model": case "day,repo": case "day,model": case "repo,model": case "day,repo,model": return true;
                default: return false;
            }
        }

        private static bool IsAsciiAlphaNumeric(char character) => (character >= 'a' && character <= 'z')
            || (character >= 'A' && character <= 'Z') || (character >= '0' && character <= '9');
    }

    public sealed class DetailsTotals
    {
        public long Requests { get; set; }
        public decimal? Credits { get; set; }
        public long InputTokens { get; set; }
        public long OutputTokens { get; set; }
        // Distinct chats are never added across groups, so this is null when it cannot be stated exactly.
        public long? Chats { get; set; }
    }

    // Repository is owner/repo, "unknown", or DetailsQuery.UnsupportedOrigin; other recorded origins are never carried.
    public sealed class DetailsRow
    {
        public DateTimeOffset? DayUtc { get; set; }
        public string? Repository { get; set; }
        public string? Model { get; set; }
        public DetailsTotals Totals { get; set; } = new DetailsTotals();
    }

    // Local analytics only; never combined with GitHub account figures.
    public sealed class DetailsReport
    {
        public string Status { get; set; } = "unavailable";
        public string? ErrorCode { get; set; }
        public DateTimeOffset? AsOfUtc { get; set; }
        public DetailsQuery? Query { get; set; }
        public DetailsRow[] Rows { get; set; } = Array.Empty<DetailsRow>();
        public DetailsTotals? Total { get; set; }
        public bool RowsCapped { get; set; }
    }
}
