using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CopilotUsageMeter.Contracts;
using CopilotUsageMeter.Core;
using Newtonsoft.Json;

namespace CopilotUsageMeter.Readers.ChatHistoryCli
{
    public sealed class ChatHistoryReader : IHistoryReader
    {
        private readonly string? executable;
        private readonly string? root;
        private readonly bool invalidConfiguration;
        private readonly IChatHistoryProcessRunner runner;
        private readonly IClock clock;

        public ChatHistoryReader(string applicationDirectory, string? executableOverride = null, string? storeRoot = null,
            IChatHistoryProcessRunner? runner = null, IClock? clock = null)
        {
            this.runner = runner ?? new ChatHistoryProcessRunner();
            this.clock = clock ?? new SystemClock();
            try
            {
                if (!Path.IsPathRooted(applicationDirectory)
                    || (executableOverride != null && !Path.IsPathRooted(executableOverride))
                    || (storeRoot != null && !Path.IsPathRooted(storeRoot)))
                {
                    invalidConfiguration = true;
                    return;
                }
                var candidate = executableOverride ?? Path.Combine(applicationDirectory, "tools", "chathistory", "chathistory.exe");
                if (File.Exists(candidate)) executable = Path.GetFullPath(candidate);
                root = storeRoot == null ? null : Path.GetFullPath(storeRoot);
            }
            catch (Exception error) when (error is ArgumentException || error is NotSupportedException
                || error is IOException || error is UnauthorizedAccessException || error is System.Security.SecurityException)
            { invalidConfiguration = true; Diagnostics.Warning("history.reader", "history_invalid_configuration", error); }
        }

        public async Task<LocalReadResult> ReadAsync(LocalHistoryRequest request, CancellationToken cancellationToken)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            cancellationToken.ThrowIfCancellationRequested();
            var result = new LocalReadResult();
            if (invalidConfiguration || executable == null)
            {
                result.ErrorCode = invalidConfiguration ? "history_invalid_configuration" : "history_tool_missing";
                result.CurrentChatErrorCode = result.ErrorCode;
                return result;
            }

            var monthly = await InvokeAsync(new[] { "usage-breakdown", "--from", Iso(request.FromUtc), "--to", Iso(request.ToUtc),
                "--group-by", request.IncludeDays ? "day" : "repo", "--limit", request.IncludeDays ? "100" : "500" }, cancellationToken).ConfigureAwait(false);
            result.ErrorCode = monthly.ErrorCode;
            if (monthly.Envelope != null)
            {
                try { ReadMonth(monthly.Envelope, request, result); }
                catch (Exception error) when (error is FormatException || error is OverflowException || error is ArgumentException)
                { Diagnostics.Warning("history.reader", "history_invalid_data", error); result.ErrorCode = "history_invalid_data"; }
                if (result.Status == "available") result.AsOfUtc = clock.UtcNow.ToUniversalTime();
            }

            var current = await ReadCurrentChatAsync(request.ActiveSessionId, cancellationToken).ConfigureAwait(false);
            result.CurrentChat = current.CurrentChat;
            result.CurrentChatTotal = current.Total;
            result.CurrentChatErrorCode = current.ErrorCode;
            return result;
        }

        public async Task<LocalCurrentChatReadResult> ReadCurrentChatAsync(string? activeSessionId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = new LocalReadResult();
            if (invalidConfiguration || executable == null)
                result.CurrentChatErrorCode = invalidConfiguration ? "history_invalid_configuration" : "history_tool_missing";
            else if (string.IsNullOrWhiteSpace(activeSessionId))
                result.CurrentChatErrorCode = "history_session_unavailable";
            else if (!ChatHistoryProcessRunner.IsValidSessionId(activeSessionId!))
                result.CurrentChatErrorCode = "history_invalid_session";
            else
            {
                var turns = await InvokeAsync(new[] { "usage-turns", activeSessionId!, "--limit", "20" }, cancellationToken).ConfigureAwait(false);
                result.CurrentChatErrorCode = turns.ErrorCode;
                if (turns.Envelope != null)
                {
                    try { ReadTurns(turns.Envelope, result); }
                    catch (Exception error) when (error is FormatException || error is OverflowException)
                    { Diagnostics.Warning("history.reader", "history_invalid_data", error); result.CurrentChatErrorCode = "history_invalid_data"; }
                    if (result.CurrentChat.Status == "available") result.CurrentChat.AsOfUtc = clock.UtcNow.ToUniversalTime();
                }
            }
            return new LocalCurrentChatReadResult
            {
                CurrentChat = result.CurrentChat, Total = result.CurrentChatTotal, ErrorCode = result.CurrentChatErrorCode
            };
        }

        private async Task<Invocation> InvokeAsync(IEnumerable<string> arguments, CancellationToken cancellationToken)
        {
            var args = arguments.ToList();
            if (root != null) { args.Add("--root"); args.Add(root); }
            ChatHistoryProcessResult process;
            try { process = await runner.RunAsync(executable!, args, cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException
                || error is System.ComponentModel.Win32Exception || error is TimeoutException)
            { Diagnostics.Warning("history.reader", "history_process_failed", error); return new Invocation { ErrorCode = "history_process_failed" }; }
            cancellationToken.ThrowIfCancellationRequested();
            if (process.ErrorCode != null)
            {
                var code = SafeProcessError(process.ErrorCode);
                Diagnostics.Warning("history.reader", code);
                return new Invocation { ErrorCode = code };
            }
            if (process.ExitCode != 0)
            {
                Diagnostics.Warning("history.reader", "history_command_failed");
                return new Invocation { ErrorCode = "history_command_failed" };
            }
            if (process.StandardOutput == null || process.StandardOutput.Length > ChatHistoryProcessRunner.MaximumOutputCharacters)
                return new Invocation { ErrorCode = "history_output_limit" };
            try
            {
                var envelope = JsonConvert.DeserializeObject<Envelope>(process.StandardOutput, new JsonSerializerSettings
                {
                    TypeNameHandling = TypeNameHandling.None,
                    MetadataPropertyHandling = MetadataPropertyHandling.Ignore,
                    MissingMemberHandling = MissingMemberHandling.Ignore,
                    DateParseHandling = DateParseHandling.None,
                    FloatParseHandling = FloatParseHandling.Decimal,
                    MaxDepth = 24,
                    CheckAdditionalContent = true,
                    Converters = { new StrictScalarConverter() }
                });
                if (envelope == null || envelope.Ok != true || envelope.Command != args[0] || !envelope.Recorded.HasValue)
                    return new Invocation { ErrorCode = "history_invalid_data" };
                return new Invocation { Envelope = envelope };
            }
            catch (Exception error) when (error is JsonException || error is FormatException || error is OverflowException)
            {
                Diagnostics.Warning("history.reader", "history_invalid_data", error);
                return new Invocation { ErrorCode = "history_invalid_data" };
            }
        }

        private void ReadMonth(Envelope envelope, LocalHistoryRequest request, LocalReadResult result)
        {
            if (envelope.Recorded != true) { result.ErrorCode = "history_not_recorded"; return; }
            var total = Totals(envelope.Total);
            var limit = request.IncludeDays ? 100 : 500;
            if (envelope.Limit != limit || envelope.Rows == null || envelope.Count != envelope.Rows.Length
                || envelope.Rows.Length > limit || envelope.GroupBy == null
                || envelope.GroupBy.Length != 1 || envelope.GroupBy[0] != (request.IncludeDays ? "day" : "repo")) throw new FormatException();
            if (!request.IncludeDays)
            {
                var repositories = new Dictionary<string, LocalUsageTotals>(StringComparer.Ordinal);
                foreach (var row in envelope.Rows)
                {
                    if (row == null || string.IsNullOrWhiteSpace(row.Repo) || row.Repo!.Length > 400) throw new FormatException();
                    repositories.Add(row.Repo, Totals(row));
                }
                var mapping = new LocalRecordedRepositoryMap(repositories, envelope.Truncated != false, total);
                result.RecordedRepositories = mapping;
                if (request.RecordedRepositoryKey != null)
                {
                    var repository = mapping.Lookup(request.RecordedRepositoryKey);
                    result.Repository = repository.Totals;
                    result.RepositoryStatus = repository.Status;
                    result.RepositoryErrorCode = repository.ErrorCode ?? "";
                }
                SetMonthlyAvailable(result, total);
                return;
            }
            var days = new List<LocalDailyUsage>();
            var seen = new HashSet<DateTimeOffset>();
            foreach (var row in envelope.Rows)
            {
                if (row == null || !DateTimeOffset.TryParseExact(row.Day, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal, out var day) || day < request.FromUtc || day >= request.ToUtc || !seen.Add(day))
                    throw new FormatException();
                days.Add(new LocalDailyUsage { DayUtc = day, Totals = Totals(row) });
            }
            result.Days = days.OrderBy(day => day.DayUtc).ToArray();
            result.DaysCapped = envelope.Truncated == true || envelope.Rows.Length >= 100;
            SetMonthlyAvailable(result, total);
        }

        private static void SetMonthlyAvailable(LocalReadResult result, LocalUsageTotals total)
        {
            result.Overall = total;
            result.Status = "available";
            result.ErrorCode = null;
        }

        private void ReadTurns(Envelope envelope, LocalReadResult result)
        {
            if (envelope.Recorded != true) { result.CurrentChatErrorCode = "history_not_recorded"; return; }
            var total = Totals(envelope.Total);
            if (envelope.Limit != 20 || envelope.ByTurn == null || envelope.Count != envelope.ByTurn.Length || envelope.ByTurn.Length > 20)
                throw new FormatException();
            var turns = new List<CurrentChatTurnUsage>();
            var previous = -1;
            foreach (var row in envelope.ByTurn)
            {
                if (row == null || !row.TurnIndex.HasValue || row.TurnIndex.Value <= previous || !row.TimestampsComplete.HasValue)
                    throw new FormatException();
                var usage = Totals(row.Totals);
                var first = Timestamp(row.FirstAt);
                var last = Timestamp(row.LastAt);
                if (first > last || (row.TimestampsComplete == true && (first == null || last == null))) throw new FormatException();
                turns.Add(new CurrentChatTurnUsage
                {
                    TurnIndex = row.TurnIndex.Value,
                    Credits = usage.Credits,
                    TimestampUtc = row.TimestampsComplete == true ? last : null
                });
                previous = row.TurnIndex.Value;
            }
            if (turns.Count == 0) { result.CurrentChatErrorCode = "history_no_turns"; return; }
            result.CurrentChatTotal = total;
            result.CurrentChat = new CurrentChatUsage
            {
                Status = "available",
                Turns = turns.ToArray()
            };
            result.CurrentChatErrorCode = null;
        }

        private static LocalUsageTotals Totals(TotalFields? value)
        {
            if (value == null || value.Requests == null || value.InputTokens == null || value.OutputTokens == null
                || value.Requests < 0 || value.Credits < 0 || value.InputTokens < 0 || value.OutputTokens < 0
                || value.CacheReadTokens < 0 || value.CacheWriteTokens < 0 || value.ReasoningTokens < 0) throw new FormatException();
            return new LocalUsageTotals
            {
                Credits = value.Credits, Requests = value.Requests, InputTokens = value.InputTokens, OutputTokens = value.OutputTokens,
                CacheReadTokens = value.CacheReadTokens, CacheWriteTokens = value.CacheWriteTokens, ReasoningTokens = value.ReasoningTokens
            };
        }

        private static DateTimeOffset? Timestamp(string? value)
        {
            if (value == null) return null;
            if (!(value.EndsWith("Z", StringComparison.Ordinal) || value.EndsWith("+00:00", StringComparison.Ordinal))
                || !DateTimeOffset.TryParseExact(value, new[] { "yyyy-MM-dd'T'HH:mm:ss'Z'", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'",
                    "yyyy-MM-dd'T'HH:mm:sszzz", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFzzz" }, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal, out var timestamp)
                || timestamp.Offset != TimeSpan.Zero) throw new FormatException();
            return timestamp;
        }

        private static string Iso(DateTimeOffset value) => value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

        private static string SafeProcessError(string code)
        {
            switch (code)
            {
                case "history_timeout": case "history_start_failed": case "history_process_failed":
                case "history_output_limit": case "history_invalid_configuration": return code;
                default: return "history_process_failed";
            }
        }

        private sealed class Invocation
        {
            public Envelope? Envelope { get; set; }
            public string? ErrorCode { get; set; }
        }

        private sealed class StrictScalarConverter : JsonConverter
        {
            public override bool CanConvert(Type type) => type == typeof(long?) || type == typeof(int?)
                || type == typeof(decimal?) || type == typeof(bool?);
            public override bool CanWrite => false;
            public override object? ReadJson(JsonReader reader, Type type, object? existingValue, JsonSerializer serializer)
            {
                if (reader.TokenType == JsonToken.Null) return null;
                if (type == typeof(bool?))
                {
                    if (reader.TokenType != JsonToken.Boolean) throw new JsonSerializationException();
                    return reader.Value;
                }
                if (type == typeof(decimal?))
                {
                    if (reader.TokenType != JsonToken.Integer && reader.TokenType != JsonToken.Float) throw new JsonSerializationException();
                    try { return Convert.ToDecimal(reader.Value, CultureInfo.InvariantCulture); }
                    catch (InvalidCastException) { throw new JsonSerializationException(); }
                }
                if (reader.TokenType != JsonToken.Integer) throw new JsonSerializationException();
                try
                {
                    if (type == typeof(int?)) return Convert.ToInt32(reader.Value, CultureInfo.InvariantCulture);
                    return Convert.ToInt64(reader.Value, CultureInfo.InvariantCulture);
                }
                catch (InvalidCastException) { throw new JsonSerializationException(); }
            }
            public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer) => throw new NotSupportedException();
        }

        [JsonObject(MemberSerialization.OptIn)]
        private sealed class Envelope
        {
            [JsonProperty("ok")] public bool? Ok { get; set; }
            [JsonProperty("command")] public string? Command { get; set; }
            [JsonProperty("recorded")] public bool? Recorded { get; set; }
            [JsonProperty("truncated")] public bool? Truncated { get; set; }
            [JsonProperty("total")] public TotalFields? Total { get; set; }
            [JsonProperty("rows")] public DayFields[]? Rows { get; set; }
            [JsonProperty("by_turn")] public TurnFields[]? ByTurn { get; set; }
            [JsonProperty("limit")] public int? Limit { get; set; }
            [JsonProperty("count")] public int? Count { get; set; }
            [JsonProperty("group_by")] public string[]? GroupBy { get; set; }
        }

        [JsonObject(MemberSerialization.OptIn)]
        private class TotalFields
        {
            [JsonProperty("credits")] public decimal? Credits { get; set; }
            [JsonProperty("requests")] public long? Requests { get; set; }
            [JsonProperty("input_tokens")] public long? InputTokens { get; set; }
            [JsonProperty("output_tokens")] public long? OutputTokens { get; set; }
            [JsonProperty("cache_read_tokens")] public long? CacheReadTokens { get; set; }
            [JsonProperty("cache_write_tokens")] public long? CacheWriteTokens { get; set; }
            [JsonProperty("reasoning_tokens")] public long? ReasoningTokens { get; set; }
        }

        [JsonObject(MemberSerialization.OptIn)]
        private sealed class DayFields : TotalFields
        {
            [JsonProperty("day")] public string? Day { get; set; }
            [JsonProperty("repo")] public string? Repo { get; set; }
        }

        [JsonObject(MemberSerialization.OptIn)]
        private sealed class TurnFields
        {
            [JsonProperty("turn_index")] public int? TurnIndex { get; set; }
            [JsonProperty("totals")] public TotalFields? Totals { get; set; }
            [JsonProperty("first_at")] public string? FirstAt { get; set; }
            [JsonProperty("last_at")] public string? LastAt { get; set; }
            [JsonProperty("timestamps_complete")] public bool? TimestampsComplete { get; set; }
        }
    }
}