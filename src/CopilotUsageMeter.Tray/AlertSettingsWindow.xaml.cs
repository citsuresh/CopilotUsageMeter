using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using CopilotUsageMeter.Core;

namespace CopilotUsageMeter.Tray;

// Edits a working copy of the alert settings; nothing applies until Save.
public partial class AlertSettingsWindow : Window
{
    private sealed record Row(TextBox Percent, ComboBox Channel, FrameworkElement Panel);

    private static readonly (AlertChannel Channel, string Label)[] Channels =
    {
        (AlertChannel.Notification, "Windows notification"),
        (AlertChannel.MessageWindow, "Message window"),
        (AlertChannel.Off, "Off"),
    };

    private readonly List<Row> rows = new();
    private readonly Func<AlertSettings, bool> save;
    private readonly Action<AlertChannel> sendTest;
    private CheckBox? notifyProblemsBox;

    internal AlertSettingsWindow(AlertSettings current, Func<AlertSettings, bool> save, Action<AlertChannel> sendTest)
    {
        this.save = save;
        this.sendTest = sendTest;
        InitializeComponent();
        EnabledBox.IsChecked = current.Enabled;
        AddProblemSection(current.NotifyProblems);
        foreach (var level in AlertSettings.NormalizeLevels(current.Levels)) AddRow(level.Percent, level.Channel);
    }

    // Problem notifications are independent of the usage levels, so they sit in their own section above the messages.
    private void AddProblemSection(bool notifyProblems)
    {
        notifyProblemsBox = new CheckBox { Content = "Notify me about problems", IsChecked = notifyProblems, Margin = new Thickness(0, 12, 0, 0) };
        AutomationProperties.SetName(notifyProblemsBox, "Notify me about problems");
        var description = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap, Opacity = 0.75, Margin = new Thickness(0, 4, 0, 0),
            Text = "A Windows notification when you are not signed in at launch, the saved sign-in expires, GitHub usage is unavailable or cannot be read, " +
                   "local usage cannot be read, or a background task fails. Each problem notifies once and not again for six hours; short network drops do not notify."
        };
        var panel = (System.Windows.Controls.Panel)ErrorText.Parent;
        var index = panel.Children.IndexOf(ErrorText);
        panel.Children.Insert(index, new Separator { Margin = new Thickness(0, 16, 0, 0) });
        panel.Children.Insert(index + 1, notifyProblemsBox);
        panel.Children.Insert(index + 2, description);
    }

    private void AddRow(int percent, AlertChannel channel)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        var box = new TextBox { Text = percent.ToString(CultureInfo.InvariantCulture), Width = 60, MaxLength = 3, VerticalContentAlignment = VerticalAlignment.Center };
        AutomationProperties.SetName(box, "Level percent");
        var combo = new ComboBox { Width = 180, Margin = new Thickness(0, 0, 8, 0) };
        AutomationProperties.SetName(combo, "Alert type");
        foreach (var (_, label) in Channels) combo.Items.Add(label);
        combo.SelectedIndex = Math.Max(0, Array.FindIndex(Channels, item => item.Channel == channel));
        var remove = new Button { Content = "\u2715", Padding = new Thickness(8, 2, 8, 2), ToolTip = "Remove level" };
        AutomationProperties.SetName(remove, "Remove level");
        panel.Children.Add(box);
        panel.Children.Add(new TextBlock { Text = "%", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 8, 0) });
        panel.Children.Add(combo);
        panel.Children.Add(remove);
        var row = new Row(box, combo, panel);
        remove.Click += (_, _) => { rows.Remove(row); LevelRows.Children.Remove(panel); HideMessages(); };
        rows.Add(row);
        LevelRows.Children.Add(panel);
    }

    private bool TryReadRows(out List<AlertLevel> levels)
    {
        levels = new List<AlertLevel>();
        var seen = new HashSet<int>();
        foreach (var row in rows)
        {
            if (!int.TryParse(row.Percent.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var percent) || !AlertSettings.IsValidPercent(percent))
            {
                ShowError($"Each level must be a whole number from {AlertSettings.MinimumPercent} to {AlertSettings.MaximumPercent}.");
                return false;
            }
            if (!seen.Add(percent)) { ShowError($"The level {percent}% is listed more than once."); return false; }
            levels.Add(new AlertLevel(percent, Channels[Math.Max(0, row.Channel.SelectedIndex)].Channel));
        }
        if (levels.Count > AlertSettings.MaximumLevels) { ShowError($"Use at most {AlertSettings.MaximumLevels} levels."); return false; }
        return true;
    }

    private void AddClicked(object sender, RoutedEventArgs e)
    {
        HideMessages();
        if (!int.TryParse(NewLevelBox.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var percent) || !AlertSettings.IsValidPercent(percent))
        {
            ShowError($"Enter a whole number from {AlertSettings.MinimumPercent} to {AlertSettings.MaximumPercent}.");
            return;
        }
        if (!TryReadRows(out var levels)) return;
        if (levels.Exists(level => level.Percent == percent)) { ShowError($"The level {percent}% already exists."); return; }
        if (levels.Count >= AlertSettings.MaximumLevels) { ShowError($"Use at most {AlertSettings.MaximumLevels} levels."); return; }
        levels.Add(new AlertLevel(percent, AlertChannel.Notification));
        Render(levels);
        NewLevelBox.Clear();
    }

    private void Render(IEnumerable<AlertLevel> levels)
    {
        rows.Clear();
        LevelRows.Children.Clear();
        foreach (var level in AlertSettings.NormalizeLevels(levels)) AddRow(level.Percent, level.Channel);
    }

    private void SaveClicked(object sender, RoutedEventArgs e)
    {
        HideMessages();
        if (!TryReadRows(out var levels)) return;
        var settings = new AlertSettings { Enabled = EnabledBox.IsChecked == true, NotifyProblems = notifyProblemsBox?.IsChecked == true, Levels = levels };
        Render(settings.Levels);
        ShowStatus(save(settings) ? "Saved." : "Applied for now, but the settings file could not be written.");
    }

    private void TestNotificationClicked(object sender, RoutedEventArgs e) => Test(AlertChannel.Notification, "Test notification sent. If nothing appears, check Windows notification settings and Focus Assist.");

    private void TestMessageClicked(object sender, RoutedEventArgs e) => Test(AlertChannel.MessageWindow, "Test message window shown.");

    private void Test(AlertChannel channel, string status)
    {
        HideMessages();
        try { sendTest(channel); ShowStatus(status); }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            CopilotUsageMeter.Contracts.Diagnostics.Warning("alert_settings_window", "test-failed", exception);
            ShowError("The test alert could not be shown.");
        }
    }

    private void CloseClicked(object sender, RoutedEventArgs e) => Close();

    private void ShowError(string message) { StatusText.Visibility = Visibility.Collapsed; ErrorText.Text = message; ErrorText.Visibility = Visibility.Visible; }
    private void ShowStatus(string message) { ErrorText.Visibility = Visibility.Collapsed; StatusText.Text = message; StatusText.Visibility = Visibility.Visible; }
    private void HideMessages() { ErrorText.Visibility = Visibility.Collapsed; StatusText.Visibility = Visibility.Collapsed; }
}
