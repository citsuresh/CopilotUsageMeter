using System;
using System.Collections.Generic;
using System.Globalization;
using CopilotUsageMeter.Core;
using CopilotUsageMeter.Panel.Core;

namespace CopilotUsageMeter.Tray
{
    public enum DetailsRange { Last7Days, Last30Days, Last90Days, ThisMonth, LastMonth, Custom }

    // What the filter bar holds. Dates are UTC calendar days, because the history tool groups by UTC day; CustomTo is inclusive.
    public sealed class DetailsFilterInput
    {
        public DetailsRange Range { get; set; } = DetailsRange.Last30Days;
        public DateTime? CustomFrom { get; set; }
        public DateTime? CustomTo { get; set; }
        public DetailsGrouping Grouping { get; set; } = DetailsGrouping.Day;
        public string? Repository { get; set; }
        public string? Model { get; set; }
    }

    public sealed class DetailsFilterResult
    {
        public DetailsQuery? Query { get; set; }
        public string? Error { get; set; }
        // A filtered dimension is always grouped, so the grouping can be wider than the one requested.
        public DetailsGrouping Grouping { get; set; }
    }

    public static class DetailsFilterBuilder
    {
        public static DetailsFilterResult Build(DetailsFilterInput input, DateTimeOffset now)
        {
            if (input == null) throw new ArgumentNullException(nameof(input));
            var today = new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero);
            DateTimeOffset from, to;
            switch (input.Range)
            {
                case DetailsRange.Last7Days: from = today.AddDays(-6); to = today.AddDays(1); break;
                case DetailsRange.Last30Days: from = today.AddDays(-29); to = today.AddDays(1); break;
                case DetailsRange.Last90Days: from = today.AddDays(-89); to = today.AddDays(1); break;
                case DetailsRange.ThisMonth: from = today.AddDays(1 - today.Day); to = today.AddDays(1); break;
                case DetailsRange.LastMonth:
                    to = today.AddDays(1 - today.Day);
                    from = to.AddMonths(-1);
                    break;
                case DetailsRange.Custom:
                    if (!input.CustomFrom.HasValue || !input.CustomTo.HasValue) return Fail("Choose both a start and an end date.");
                    if (input.CustomTo.Value.Date >= DateTime.MaxValue.Date) return Fail("Choose a valid end date.");
                    from = new DateTimeOffset(DateTime.SpecifyKind(input.CustomFrom.Value.Date, DateTimeKind.Utc));
                    to = new DateTimeOffset(DateTime.SpecifyKind(input.CustomTo.Value.Date, DateTimeKind.Utc)).AddDays(1);
                    if (to <= from) return Fail("The end date must not be before the start date.");
                    if (to - from > TimeSpan.FromDays(DetailsQuery.MaximumDays))
                        return Fail("Choose a range of at most " + DetailsQuery.MaximumDays.ToString(CultureInfo.InvariantCulture) + " days.");
                    break;
                default: return Fail("Choose a date range.");
            }

            var repository = string.IsNullOrWhiteSpace(input.Repository) ? null : input.Repository!.Trim();
            var model = string.IsNullOrWhiteSpace(input.Model) ? null : input.Model!.Trim();
            if (repository != null && !DetailsQuery.IsValidRepositoryFilter(repository))
                return Fail("Enter the repository as owner/name, using letters, digits, '-', '_' and '.'.");
            if (model != null && !DetailsQuery.IsValidModelFilter(model))
                return Fail("Enter the model using letters, digits and . _ - : / only, starting with a letter or digit.");

            var grouping = input.Grouping;
            if (repository != null) grouping |= DetailsGrouping.Repository;
            if (model != null) grouping |= DetailsGrouping.Model;
            if (grouping == DetailsGrouping.None) return Fail("Choose at least one way to group the results.");
            try
            {
                return new DetailsFilterResult { Query = new DetailsQuery(from, to, grouping, repository, model), Grouping = grouping };
            }
            catch (ArgumentException)
            {
                return Fail("These filters cannot be used together.");
            }
        }

        private static DetailsFilterResult Fail(string message) => new DetailsFilterResult { Error = message };
    }

    // Local, on-this-machine figures only; nothing here is combined with GitHub account data.
    public sealed class DetailsDisplay
    {
        public const string Unavailable = "unavailable";
        public const string NotStated = "n/a";

        public IReadOnlyList<string> Headers { get; set; } = Array.Empty<string>();
        public IReadOnlyList<IReadOnlyList<string>> Rows { get; set; } = Array.Empty<IReadOnlyList<string>>();
        public IReadOnlyList<string>? Total { get; set; }
        public IReadOnlyList<string> Notes { get; set; } = Array.Empty<string>();
        public string Summary { get; set; } = "";
        public string? EmptyMessage { get; set; }
        public DisplayProblem? Problem { get; set; }
        public bool HasTable => Rows.Count > 0;
    }

    public static class DetailsDisplayBuilder
    {
        public static DetailsDisplay Build(DetailsReport report, TimeZoneInfo? zone = null)
        {
            if (report == null) throw new ArgumentNullException(nameof(report));
            zone ??= TimeZoneInfo.Local;
            var display = new DetailsDisplay();
            if (report.Status != "available")
            {
                if (report.ErrorCode == "history_not_recorded")
                    display.EmptyMessage = "No local chat history has been recorded on this machine yet.";
                else
                    display.Problem = UsageDisplay.LocalProblemForCode(report.ErrorCode ?? "history_command_failed");
                if (display.EmptyMessage == null && display.Problem == null)
                    display.Problem = UsageDisplay.LocalProblemForCode("history_command_failed");
                return display;
            }

            var query = report.Query ?? throw new ArgumentException("An available report needs its query.", nameof(report));
            var byDay = (query.Grouping & DetailsGrouping.Day) != 0;
            var byRepository = (query.Grouping & DetailsGrouping.Repository) != 0;
            var byModel = (query.Grouping & DetailsGrouping.Model) != 0;
            var headers = new List<string>();
            if (byDay) headers.Add("Day (UTC)");
            if (byRepository) headers.Add("Repository");
            if (byModel) headers.Add("Model");
            headers.AddRange(new[] { "Requests", "Credits", "Input tokens", "Output tokens", "Chats" });
            display.Headers = headers;

            var rows = new List<IReadOnlyList<string>>();
            var chatsNotStated = false;
            foreach (var row in report.Rows)
            {
                var cells = new List<string>();
                if (byDay) cells.Add(row.DayUtc.HasValue ? row.DayUtc.Value.ToUniversalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "");
                if (byRepository) cells.Add(RepositoryLabel(row.Repository));
                if (byModel) cells.Add(ModelLabel(row.Model));
                AddFigures(cells, row.Totals, ref chatsNotStated);
                rows.Add(cells);
            }
            display.Rows = rows;

            var notes = new List<string>();
            if (report.Total != null)
            {
                var cells = new List<string>();
                var leading = (byDay ? 1 : 0) + (byRepository ? 1 : 0) + (byModel ? 1 : 0);
                cells.Add("Total");
                for (var index = 1; index < leading; index++) cells.Add("");
                AddFigures(cells, report.Total, ref chatsNotStated);
                display.Total = cells;
            }
            else if (rows.Count > 0)
                notes.Add("No total is shown: the result was cut off while a filter was applied, so a total would be incomplete.");
            if (report.RowsCapped)
                notes.Add("Showing the first " + DetailsQuery.MaximumRows.ToString(CultureInfo.InvariantCulture) + " rows. Narrow the range or add a filter to see the rest.");
            if (chatsNotStated)
                notes.Add("Chats is n/a where distinct chats cannot be counted exactly (combined rows, or totals across groups).");
            display.Notes = notes;

            var range = query.FromUtc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + " to "
                + query.ToUtc.AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            display.Summary = report.Rows.Length.ToString(CultureInfo.InvariantCulture) + (report.Rows.Length == 1 ? " row" : " rows")
                + " · UTC days " + range
                + (report.AsOfUtc.HasValue ? " · as of " + TimeZoneInfo.ConvertTime(report.AsOfUtc.Value, zone).ToString("d MMM yyyy, h:mm tt", CultureInfo.InvariantCulture) : "");
            if (rows.Count == 0) display.EmptyMessage = "No local usage was recorded for this selection.";
            return display;
        }

        private static void AddFigures(List<string> cells, DetailsTotals totals, ref bool chatsNotStated)
        {
            cells.Add(totals.Requests.ToString("#,##0", CultureInfo.InvariantCulture));
            cells.Add(UsageDisplay.Credits(totals.Credits, DetailsDisplay.Unavailable));
            cells.Add(totals.InputTokens.ToString("#,##0", CultureInfo.InvariantCulture));
            cells.Add(totals.OutputTokens.ToString("#,##0", CultureInfo.InvariantCulture));
            if (totals.Chats.HasValue) cells.Add(totals.Chats.Value.ToString("#,##0", CultureInfo.InvariantCulture));
            else { cells.Add(DetailsDisplay.NotStated); chatsNotStated = true; }
        }

        internal static string RepositoryLabel(string? repository) =>
            string.IsNullOrEmpty(repository) || repository == "unknown" ? "(unknown)" : repository!;

        internal static string ModelLabel(string? model) => string.IsNullOrEmpty(model) ? "(unknown)" : model!;
    }
}
