using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using CopilotUsageMeter.Contracts;
using CopilotUsageMeter.Core;
using CopilotUsageMeter.Panel.Core;
using Microsoft.Win32;

namespace CopilotUsageMeter.Tray;

// The local usage breakdown. Reads happen only when the user asks, one at a time, and are cancelled when the window closes.
// Nothing here is rebuilt by the one-second snapshot refresh, so typing and focus are never disturbed.
public partial class MainWindow
{
    // The report currently shown, kept only so it can be exported; cleared whenever the table is cleared.
    private DetailsReport? exportableReport;
    private bool exporting;

    private void SetExportable(DetailsReport? report)
    {
        exportableReport = report;
        ExportDetailsButton.Visibility = report == null ? Visibility.Collapsed : Visibility.Visible;
    }

    // The saved defaults apply when the window opens; the repository and model filters always start empty.
    private void ApplyDetailsDefaults(AppSettings saved)
    {
        DetailsRangeBox.SelectedIndex = (int)saved.DefaultRange;
        var grouping = saved.DefaultGrouping;
        DetailsDayBox.IsChecked = (grouping & DetailsGrouping.Day) != 0;
        DetailsRepositoryGroupBox.IsChecked = (grouping & DetailsGrouping.Repository) != 0;
        DetailsModelGroupBox.IsChecked = (grouping & DetailsGrouping.Model) != 0;
    }

    private void DetailsRangeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DetailsCustomPanel == null) return;
        var custom = DetailsRangeBox.SelectedIndex == (int)DetailsRange.Custom;
        DetailsCustomPanel.Visibility = custom ? Visibility.Visible : Visibility.Collapsed;
        if (!custom) return;
        var today = DateTime.UtcNow.Date;
        DetailsFromPicker.SelectedDate ??= today.AddDays(-29);
        DetailsToPicker.SelectedDate ??= today;
    }

    private void CancelDetailsClicked(object sender, RoutedEventArgs e) => detailsRead?.Cancel();

    private async void ShowDetailsClicked(object sender, RoutedEventArgs e)
    {
        if (detailsRead != null || exporting) return;
        var grouping = DetailsGrouping.None;
        if (DetailsDayBox.IsChecked == true) grouping |= DetailsGrouping.Day;
        if (DetailsRepositoryGroupBox.IsChecked == true) grouping |= DetailsGrouping.Repository;
        if (DetailsModelGroupBox.IsChecked == true) grouping |= DetailsGrouping.Model;
        var built = DetailsFilterBuilder.Build(new DetailsFilterInput
        {
            Range = (DetailsRange)Math.Max(0, DetailsRangeBox.SelectedIndex),
            CustomFrom = DetailsFromPicker.SelectedDate,
            CustomTo = DetailsToPicker.SelectedDate,
            Grouping = grouping,
            Repository = DetailsRepositoryBox.Text,
            Model = DetailsModelBox.Text
        }, DateTimeOffset.UtcNow);
        if (built.Query == null)
        {
            DetailsResultsHost.Children.Clear();
            SetExportable(null);
            ShowDetailsStatus(built.Error ?? "These filters cannot be used.", error: true);
            return;
        }

        // A filtered dimension is always grouped, so show the grouping that was actually used.
        DetailsDayBox.IsChecked = (built.Grouping & DetailsGrouping.Day) != 0;
        DetailsRepositoryGroupBox.IsChecked = (built.Grouping & DetailsGrouping.Repository) != 0;
        DetailsModelGroupBox.IsChecked = (built.Grouping & DetailsGrouping.Model) != 0;

        var source = new CancellationTokenSource();
        detailsRead = source;
        ShowDetailsButton.IsEnabled = false;
        CancelDetailsButton.Visibility = Visibility.Visible;
        DetailsResultsHost.Children.Clear();
        SetExportable(null);
        ShowDetailsStatus("Reading local history…", error: false);
        UpdateBusyBar();

        DetailsReport? report = null;
        string? failure = null;
        try { report = await readDetails(built.Query, source.Token); }
        catch (OperationCanceledException) { failure = "Cancelled."; }
        catch (Exception exception)
        {
            Diagnostics.Warning("tray.window", "details_read_failed", exception);
            failure = "Reading local history failed. Try again.";
        }
        finally
        {
            detailsRead = null;
            source.Dispose();
        }
        if (closed) return;

        ShowDetailsButton.IsEnabled = true;
        CancelDetailsButton.Visibility = Visibility.Collapsed;
        UpdateBusyBar();
        if (report == null)
        {
            ShowDetailsStatus(failure ?? "Reading local history failed. Try again.", error: failure != "Cancelled.");
            return;
        }
        DetailsStatusText.Visibility = Visibility.Collapsed;
        RenderDetails(DetailsDisplayBuilder.Build(report));
        SetExportable(report.Status == "available" && report.Query != null ? report : null);
    }

    private async void ExportDetailsClicked(object sender, RoutedEventArgs e)
    {
        var report = exportableReport;
        if (report == null || exporting) return;
        if (DetailsExporter.ContainsRepositoryNames(report)
            && MessageBox.Show(this,
                "This file will contain repository names (owner/repo), including private repositories. Local usage is never uploaded, but anyone you share the file with will see these names.\n\nContinue?",
                "Export usage breakdown", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK)
            return;

        var dialog = new SaveFileDialog
        {
            Title = "Export usage breakdown",
            Filter = "CSV file (*.csv)|*.csv|JSON file (*.json)|*.json",
            DefaultExt = ".csv",
            AddExtension = true,
            CheckPathExists = true,
            OverwritePrompt = true,
            FileName = DetailsExporter.SuggestedFileName(report)
        };
        var folder = settings.Current.LastExportFolder;
        if (folder != null) dialog.InitialDirectory = folder;
        if (dialog.ShowDialog(this) != true) return;

        var path = dialog.FileName;
        var format = DetailsExporter.FormatForPath(path);
        exporting = true;
        ExportDetailsButton.IsEnabled = false;
        ShowDetailsButton.IsEnabled = false;
        string? failure = null;
        try
        {
            var content = DetailsExporter.Build(format, report, getSnapshot().Account, DateTimeOffset.UtcNow);
            await Task.Run(() => DetailsExporter.WriteAtomically(path, content, format));
        }
        catch (Exception exception)
        {
            Diagnostics.Warning("tray.window", "details_export_failed", exception);
            failure = "Could not save the file. Check the location and your permissions, then try again.";
        }
        finally { exporting = false; }
        if (failure == null) RememberExportFolder(path);
        if (closed) return;

        ExportDetailsButton.IsEnabled = true;
        ShowDetailsButton.IsEnabled = true;
        ShowDetailsStatus(failure ?? "Exported to " + Path.GetFileName(path) + ".", error: failure != null);
    }

    // Only the folder is remembered, after a successful export; a failed save of the setting is logged by the store and ignored.
    private void RememberExportFolder(string path)
    {
        string? directory;
        try { directory = Path.GetDirectoryName(Path.GetFullPath(path)); }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException) { return; }
        if (!string.IsNullOrEmpty(directory)) settings.Update(saved => saved.LastExportFolder = directory);
    }

    private void ShowDetailsStatus(string message, bool error)
    {
        DetailsStatusText.Text = message;
        if (error) DetailsStatusText.Foreground = Red; else DetailsStatusText.ClearValue(TextBlock.ForegroundProperty);
        DetailsStatusText.FontWeight = error ? FontWeights.SemiBold : FontWeights.Normal;
        DetailsStatusText.Visibility = Visibility.Visible;
    }

    private void RenderDetails(DetailsDisplay result)
    {
        var host = DetailsResultsHost;
        host.Children.Clear();
        if (result.Problem != null)
        {
            host.Children.Add(ProblemBlock(result.Problem, 12));
            return;
        }
        if (result.Summary.Length > 0) host.Children.Add(Text(result.Summary, muted: true, size: 11));
        if (result.EmptyMessage != null) host.Children.Add(Text(result.EmptyMessage, margin: new Thickness(0, 6, 0, 0)));
        if (result.HasTable) host.Children.Add(DetailsTable(result));
        foreach (var note in result.Notes) host.Children.Add(Text(note, muted: true, size: 11, margin: new Thickness(0, 6, 0, 0)));
    }

    // One grid for header, rows and total so the columns line up. The last five columns are figures and are right-aligned.
    private static FrameworkElement DetailsTable(DetailsDisplay result)
    {
        var grid = new Grid { Margin = new Thickness(0, 6, 0, 0) };
        var columns = result.Headers.Count;
        var firstFigure = columns - 5;
        for (var column = 0; column < columns; column++) grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var row = 0;
        void AddRow(System.Collections.Generic.IReadOnlyList<string> cells, bool header, bool total)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            for (var column = 0; column < columns; column++)
            {
                var cell = Text(cells[column], muted: header, bold: header || total, size: 12, margin: new Thickness(0, 1, 16, 1));
                cell.TextWrapping = TextWrapping.NoWrap;
                if (column >= firstFigure) { cell.TextAlignment = TextAlignment.Right; cell.HorizontalAlignment = HorizontalAlignment.Right; }
                Grid.SetRow(cell, row);
                Grid.SetColumn(cell, column);
                grid.Children.Add(cell);
            }
            row++;
        }

        AddRow(result.Headers, header: true, total: false);
        foreach (var cells in result.Rows) AddRow(cells, header: false, total: false);
        if (result.Total != null) AddRow(result.Total, header: false, total: true);
        AutomationProperties.SetName(grid, "Local usage breakdown table");
        return grid;
    }
}
