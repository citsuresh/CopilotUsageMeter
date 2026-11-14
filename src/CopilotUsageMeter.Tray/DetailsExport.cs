using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using CopilotUsageMeter.Contracts;
using CopilotUsageMeter.Core;

namespace CopilotUsageMeter.Tray
{
    public enum DetailsExportFormat { Csv, Json }

    // Builds the files written by "Export". Local figures (this machine only) and GitHub account figures (all machines) are
    // written as two separate sections and never summed. The account section carries no login. Unavailable values are empty
    // (CSV) or null (JSON), never zero.
    public static class DetailsExporter
    {
        public const string LocalScope = "Recorded on this machine only. Not GitHub billing, and not part of the account figures.";
        public const string AccountScope = "Reported by GitHub for the whole account across all machines. Not added to the local figures.";
        private const string UnavailableNote = "An empty cell means the value is unavailable.";

        public static DetailsExportFormat FormatForPath(string? path) =>
            string.Equals(Path.GetExtension(path), ".json", StringComparison.OrdinalIgnoreCase) ? DetailsExportFormat.Json : DetailsExportFormat.Csv;

        public static string SuggestedFileName(DetailsReport report)
        {
            if (report == null) throw new ArgumentNullException(nameof(report));
            var query = RequireQuery(report);
            return "copilot-usage-breakdown-" + query.FromUtc.ToString("yyyyMMdd", CultureInfo.InvariantCulture)
                + "-" + query.ToUtc.AddDays(-1).ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        }

        // Repository names, including private ones, are exported whenever the rows are grouped by repository.
        public static bool ContainsRepositoryNames(DetailsReport report) =>
            report != null && report.Query != null && (report.Query.Grouping & DetailsGrouping.Repository) != 0 && report.Rows.Length > 0;

        public static string ToCsv(DetailsReport report, AccountUsage account, DateTimeOffset exportedUtc)
        {
            if (report == null) throw new ArgumentNullException(nameof(report));
            if (account == null) throw new ArgumentNullException(nameof(account));
            var query = RequireQuery(report);
            var lines = new List<string>();
            void Line(params string[] cells)
            {
                var builder = new StringBuilder();
                for (var index = 0; index < cells.Length; index++)
                {
                    if (index > 0) builder.Append(',');
                    builder.Append(CsvText(cells[index]));
                }
                lines.Add(builder.ToString());
            }
            void Numbers(IEnumerable<string> text, DetailsTotals totals)
            {
                var builder = new StringBuilder();
                foreach (var cell in text) builder.Append(CsvText(cell)).Append(',');
                builder.Append(Whole(totals.Requests)).Append(',').Append(Decimal(totals.Credits)).Append(',')
                    .Append(Whole(totals.InputTokens)).Append(',').Append(Whole(totals.OutputTokens)).Append(',').Append(Whole(totals.Chats));
                lines.Add(builder.ToString());
            }

            var byDay = (query.Grouping & DetailsGrouping.Day) != 0;
            var byRepository = (query.Grouping & DetailsGrouping.Repository) != 0;
            var byModel = (query.Grouping & DetailsGrouping.Model) != 0;

            Line("Copilot Usage Meter export");
            Line("Exported (UTC)", Timestamp(exportedUtc));
            Line();
            Line("Local usage (this machine only)");
            Line("Scope", LocalScope);
            Line("From (UTC day)", Day(query.FromUtc));
            Line("To (UTC day, inclusive)", Day(query.ToUtc.AddDays(-1)));
            Line("Grouped by", string.Join(", ", GroupNames(query.Grouping)));
            if (query.RepositoryFilter != null) Line("Repository filter", query.RepositoryFilter);
            if (query.ModelFilter != null) Line("Model filter", query.ModelFilter);
            Line("As of (UTC)", report.AsOfUtc.HasValue ? Timestamp(report.AsOfUtc.Value) : "");
            Line("Rows capped", report.RowsCapped ? "Yes" : "No");
            Line("Total", report.Total != null ? "Included" : "Not available: the result was cut off while a filter was applied");
            Line("Note", UnavailableNote + " Chats is empty where distinct chats cannot be counted exactly.");
            Line();
            var headers = new List<string>();
            if (byDay) headers.Add("Day (UTC)");
            if (byRepository) headers.Add("Repository");
            if (byModel) headers.Add("Model");
            headers.AddRange(new[] { "Requests", "Credits", "Input tokens", "Output tokens", "Chats" });
            Line(headers.ToArray());
            foreach (var row in report.Rows)
            {
                var text = new List<string>();
                if (byDay) text.Add(row.DayUtc.HasValue ? Day(row.DayUtc.Value) : "");
                if (byRepository) text.Add(DetailsDisplayBuilder.RepositoryLabel(row.Repository));
                if (byModel) text.Add(DetailsDisplayBuilder.ModelLabel(row.Model));
                Numbers(text, row.Totals);
            }
            if (report.Total != null)
            {
                var text = new List<string>();
                var leading = (byDay ? 1 : 0) + (byRepository ? 1 : 0) + (byModel ? 1 : 0);
                text.Add("Total");
                for (var index = 1; index < leading; index++) text.Add("");
                Numbers(text, report.Total);
            }

            Line();
            Line("GitHub account usage (all machines)");
            Line("Scope", AccountScope);
            var available = account.Status == "available";
            Line("Status", account.Status ?? "");
            Line("Unit", available ? account.Unit ?? "" : "");
            Line("Unlimited", available ? (account.IsUnlimited ? "Yes" : "No") : "");
            Line("Allowance", available && !account.IsUnlimited ? Decimal(account.Allowance) : "");
            Line("Consumed", available ? Decimal(account.Consumed) : "");
            Line("Reported remaining", available ? Decimal(account.Remaining) : "");
            Line("Resets (UTC)", available && account.ResetUtc.HasValue ? Timestamp(account.ResetUtc.Value) : "");
            Line("Last sync (UTC)", available && account.AsOfUtc.HasValue ? Timestamp(account.AsOfUtc.Value) : "");
            Line("Note", UnavailableNote);
            return string.Join("\r\n", lines) + "\r\n";
        }

        public static string ToJson(DetailsReport report, AccountUsage account, DateTimeOffset exportedUtc)
        {
            if (report == null) throw new ArgumentNullException(nameof(report));
            if (account == null) throw new ArgumentNullException(nameof(account));
            var query = RequireQuery(report);
            var byDay = (query.Grouping & DetailsGrouping.Day) != 0;
            var byRepository = (query.Grouping & DetailsGrouping.Repository) != 0;
            var byModel = (query.Grouping & DetailsGrouping.Model) != 0;

            using var stream = new MemoryStream();
            using (var json = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
            {
                json.WriteStartObject();
                json.WriteString("format", "copilot-usage-meter-export");
                json.WriteNumber("version", 1);
                json.WriteString("exportedUtc", Timestamp(exportedUtc));

                json.WriteStartObject("local");
                json.WriteString("scope", LocalScope);
                json.WriteString("fromUtcDay", Day(query.FromUtc));
                json.WriteString("toUtcDayInclusive", Day(query.ToUtc.AddDays(-1)));
                json.WriteStartArray("groupedBy");
                foreach (var name in GroupNames(query.Grouping)) json.WriteStringValue(name);
                json.WriteEndArray();
                json.WriteString("repositoryFilter", query.RepositoryFilter);
                json.WriteString("modelFilter", query.ModelFilter);
                if (report.AsOfUtc.HasValue) json.WriteString("asOfUtc", Timestamp(report.AsOfUtc.Value)); else json.WriteNull("asOfUtc");
                json.WriteBoolean("rowsCapped", report.RowsCapped);
                json.WriteStartArray("rows");
                foreach (var row in report.Rows)
                {
                    json.WriteStartObject();
                    if (byDay) { if (row.DayUtc.HasValue) json.WriteString("dayUtc", Day(row.DayUtc.Value)); else json.WriteNull("dayUtc"); }
                    if (byRepository) json.WriteString("repository", DetailsDisplayBuilder.RepositoryLabel(row.Repository));
                    if (byModel) json.WriteString("model", DetailsDisplayBuilder.ModelLabel(row.Model));
                    WriteFigures(json, row.Totals);
                    json.WriteEndObject();
                }
                json.WriteEndArray();
                if (report.Total != null)
                {
                    json.WriteStartObject("total");
                    WriteFigures(json, report.Total);
                    json.WriteEndObject();
                }
                else json.WriteNull("total");
                json.WriteEndObject();

                var available = account.Status == "available";
                json.WriteStartObject("githubAccount");
                json.WriteString("scope", AccountScope);
                json.WriteString("status", account.Status);
                if (available) json.WriteString("unit", account.Unit); else json.WriteNull("unit");
                if (available) json.WriteBoolean("unlimited", account.IsUnlimited); else json.WriteNull("unlimited");
                WriteDecimal(json, "allowance", available && !account.IsUnlimited ? account.Allowance : null);
                WriteDecimal(json, "consumed", available ? account.Consumed : null);
                WriteDecimal(json, "reportedRemaining", available ? account.Remaining : null);
                WriteTimestamp(json, "resetsUtc", available ? account.ResetUtc : null);
                WriteTimestamp(json, "lastSyncUtc", available ? account.AsOfUtc : null);
                json.WriteEndObject();

                json.WriteEndObject();
            }
            return new UTF8Encoding(false).GetString(stream.ToArray()) + "\r\n";
        }

        public static string Build(DetailsExportFormat format, DetailsReport report, AccountUsage account, DateTimeOffset exportedUtc) =>
            format == DetailsExportFormat.Json ? ToJson(report, account, exportedUtc) : ToCsv(report, account, exportedUtc);

        // Writes a sibling temporary file and moves it over the target, so a failed export never damages an existing file.
        public static void WriteAtomically(string path, string content, DetailsExportFormat format)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("A file path is required.", nameof(path));
            var bytes = new UTF8Encoding(format == DetailsExportFormat.Csv).GetPreamble();
            var body = new UTF8Encoding(false).GetBytes(content);
            var all = new byte[bytes.Length + body.Length];
            Buffer.BlockCopy(bytes, 0, all, 0, bytes.Length);
            Buffer.BlockCopy(body, 0, all, bytes.Length, body.Length);
            var temporary = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, ".cum-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    stream.Write(all, 0, all.Length);
                    stream.Flush(true);
                }
                File.Move(temporary, path, overwrite: true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        private static DetailsQuery RequireQuery(DetailsReport report)
        {
            if (report.Status != "available" || report.Query == null) throw new ArgumentException("Only an available report can be exported.", nameof(report));
            return report.Query;
        }

        private static IEnumerable<string> GroupNames(DetailsGrouping grouping)
        {
            if ((grouping & DetailsGrouping.Day) != 0) yield return "day";
            if ((grouping & DetailsGrouping.Repository) != 0) yield return "repository";
            if ((grouping & DetailsGrouping.Model) != 0) yield return "model";
        }

        private static void WriteFigures(Utf8JsonWriter json, DetailsTotals totals)
        {
            json.WriteNumber("requests", totals.Requests);
            if (totals.Credits.HasValue) json.WriteNumber("credits", totals.Credits.Value); else json.WriteNull("credits");
            json.WriteNumber("inputTokens", totals.InputTokens);
            json.WriteNumber("outputTokens", totals.OutputTokens);
            if (totals.Chats.HasValue) json.WriteNumber("chats", totals.Chats.Value); else json.WriteNull("chats");
        }

        private static void WriteDecimal(Utf8JsonWriter json, string name, decimal? value)
        {
            if (value.HasValue) json.WriteNumber(name, value.Value); else json.WriteNull(name);
        }

        private static void WriteTimestamp(Utf8JsonWriter json, string name, DateTimeOffset? value)
        {
            if (value.HasValue) json.WriteString(name, Timestamp(value.Value)); else json.WriteNull(name);
        }

        private static bool StartsLikeFormula(string text)
        {
            foreach (var character in text)
            {
                if (character == '\t' || character == '\r' || character == '\n') return true;
                if (char.IsWhiteSpace(character)) continue;
                return character == '=' || character == '+' || character == '-' || character == '@';
            }
            return false;
        }

        private static string Day(DateTimeOffset value) => value.ToUniversalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        private static string Timestamp(DateTimeOffset value) => value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

        private static string Whole(long value) => value.ToString(CultureInfo.InvariantCulture);

        private static string Whole(long? value) => value.HasValue ? Whole(value.Value) : "";

        private static string Decimal(decimal? value) => value.HasValue ? value.Value.ToString("0.##########", CultureInfo.InvariantCulture) : "";

        // Quotes when needed, and neutralises text a spreadsheet would run as a formula (judged by its first non-blank character).
        private static string CsvText(string? value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            var text = value!;
            if (StartsLikeFormula(text)) text = "'" + text;
            if (text.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0 || text[0] == ' ' || text[text.Length - 1] == ' ')
                text = "\"" + text.Replace("\"", "\"\"") + "\"";
            return text;
        }
    }
}
