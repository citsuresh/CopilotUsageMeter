using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace CopilotUsageMeter.Core
{
    public sealed class LocalRecordedRepositoryResult
    {
        public string Status { get; set; } = "unavailable";
        public string? ErrorCode { get; set; }
        public string Scope => "recorded-repository";
        public LocalUsageTotals? Totals { get; set; }
    }

    // Domain-only mapping, never a transport payload. Each projection receives detached totals.
    public sealed class LocalRecordedRepositoryMap
    {
        private readonly Dictionary<string, LocalUsageTotals> entries;
        private readonly LocalUsageTotals knownEmpty;

        public LocalRecordedRepositoryMap(IEnumerable<KeyValuePair<string, LocalUsageTotals>> entries,
            bool isTruncated, LocalUsageTotals overall)
        {
            if (entries == null) throw new ArgumentNullException(nameof(entries));
            if (overall == null) throw new ArgumentNullException(nameof(overall));
            this.entries = new Dictionary<string, LocalUsageTotals>(StringComparer.Ordinal);
            foreach (var entry in entries) this.entries.Add(entry.Key, Clone(entry.Value));
            IsTruncated = isTruncated;
            knownEmpty = new LocalUsageTotals
            {
                Credits = overall.Credits.HasValue ? 0m : (decimal?)null,
                Requests = 0, InputTokens = 0, OutputTokens = 0,
                CacheReadTokens = overall.CacheReadTokens.HasValue ? 0L : (long?)null,
                CacheWriteTokens = overall.CacheWriteTokens.HasValue ? 0L : (long?)null,
                ReasoningTokens = overall.ReasoningTokens.HasValue ? 0L : (long?)null
            };
        }

        public string Scope => "recorded-repository";
        public bool IsTruncated { get; }

        public IReadOnlyDictionary<string, LocalUsageTotals> GetSnapshot() => new ReadOnlyDictionary<string, LocalUsageTotals>(
            entries.ToDictionary(pair => pair.Key, pair => Clone(pair.Value), StringComparer.Ordinal));

        public LocalRecordedRepositoryResult Lookup(string? repositoryKey)
        {
            if (!ValidRemoteKey(repositoryKey)) return Unavailable("repository_key_unavailable");
            var matches = entries.Where(pair => string.Equals(pair.Key, repositoryKey, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (matches.Length > 1) return Unavailable("repository_key_ambiguous");
            if (matches.Length == 1) return new LocalRecordedRepositoryResult { Status = "available", Totals = Clone(matches[0].Value) };
            if (IsTruncated) return Unavailable("repository_groups_capped");
            return new LocalRecordedRepositoryResult { Status = "available", Totals = Clone(knownEmpty) };
        }

        private static bool ValidRemoteKey(string? key)
        {
            if (key == null) return false;
            var parts = key.Split('/');
            if (parts.Length != 2 || parts[0].Length == 0 || parts[0].Length > 39 || parts[1].Length == 0 || parts[1].Length > 100
                || parts[1] == "." || parts[1] == "..") return false;
            return parts[0].All(character => IsAsciiAlphaNumeric(character) || character == '-')
                && parts[1].All(character => IsAsciiAlphaNumeric(character) || character == '-' || character == '_' || character == '.');
        }

        private static bool IsAsciiAlphaNumeric(char character) => (character >= 'a' && character <= 'z')
            || (character >= 'A' && character <= 'Z') || (character >= '0' && character <= '9');

        private static LocalRecordedRepositoryResult Unavailable(string code) => new LocalRecordedRepositoryResult { ErrorCode = code };

        private static LocalUsageTotals Clone(LocalUsageTotals value) => new LocalUsageTotals
        {
            Credits = value.Credits, Requests = value.Requests, InputTokens = value.InputTokens, OutputTokens = value.OutputTokens,
            CacheReadTokens = value.CacheReadTokens, CacheWriteTokens = value.CacheWriteTokens, ReasoningTokens = value.ReasoningTokens
        };
    }
}