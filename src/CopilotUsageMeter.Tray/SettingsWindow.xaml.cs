using System;
using System.ComponentModel;
using System.IO;
using System.Windows;
using CopilotUsageMeter.Contracts;

namespace CopilotUsageMeter.Tray;

// Edits the tray preferences. Nothing is written until Apply or Save, and the page is validated first.
public partial class SettingsWindow : Window
{
    private readonly Func<Action<AppSettings>, bool> update;
    private readonly Action openAlerts;
    private readonly Action openDataFolder;
    private readonly Action forgotten;
    private bool? startupEnabled;
    private bool forgetRemembered;

    internal SettingsWindow(AppSettings current, Func<Action<AppSettings>, bool> update, Action openAlerts, Action openDataFolder, Action forgotten)
    {
        this.forgotten = forgotten;
        this.update = update;
        this.openAlerts = openAlerts;
        this.openDataFolder = openDataFolder;
        InitializeComponent();
        ScheduleText.Text = RefreshScheduleText.Describe();
        try { startupEnabled = StartWithWindows.IsEnabled(); }
        catch (Exception exception) when (exception is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            Diagnostics.Warning("settings_window", "startup_state_unreadable", exception);
        }
        StartupBox.IsChecked = startupEnabled == true;
        if (startupEnabled == null)
        {
            StartupBox.IsEnabled = false;
            StartupBox.ToolTip = "The current setting could not be read.";
        }
        Display(current);
    }

    private void Display(AppSettings settings)
    {
        RangeBox.SelectedIndex = (int)settings.DefaultRange;
        DayBox.IsChecked = settings.GroupByDay;
        RepositoryBox.IsChecked = settings.GroupByRepository;
        ModelBox.IsChecked = settings.GroupByModel;
    }

    private bool Apply()
    {
        HideMessages();
        var range = (DetailsRange)Math.Max(0, RangeBox.SelectedIndex);
        var day = DayBox.IsChecked == true;
        var repository = RepositoryBox.IsChecked == true;
        var model = ModelBox.IsChecked == true;
        if (!day && !repository && !model)
        {
            ShowError("Choose at least one way to group the default details.");
            return false;
        }

        var startupChanged = false;
        if (startupEnabled.HasValue && (StartupBox.IsChecked == true) != startupEnabled.Value)
        {
            var wanted = StartupBox.IsChecked == true;
            try
            {
                StartWithWindows.Set(wanted, Environment.ProcessPath);
                startupEnabled = wanted;
                startupChanged = true;
            }
            catch (Exception exception) when (exception is System.Security.SecurityException or UnauthorizedAccessException or IOException or InvalidOperationException)
            {
                Diagnostics.Error("settings_window", "startup_toggle_failed", exception);
                StartupBox.IsChecked = startupEnabled;
                ShowError("Start with Windows could not be changed, so nothing was saved.");
                return false;
            }
        }

        var forget = forgetRemembered;
        var saved = update(settings =>
        {
            settings.DefaultRange = range;
            settings.GroupByDay = day;
            settings.GroupByRepository = repository;
            settings.GroupByModel = model;
            if (forget)
            {
                settings.DetailsPlacement = null;
                settings.LastExportFolder = null;
            }
        });
        if (!saved)
        {
            ShowError(startupChanged
                ? "Start with Windows was changed, but the other settings could not be saved. Check that your user profile folder is writable."
                : "The settings could not be saved. Check that your user profile folder is writable.");
            return false;
        }
        forgetRemembered = false;
        if (forget) forgotten();
        ShowStatus("Saved.");
        return true;
    }

    private void ApplyClicked(object sender, RoutedEventArgs e) => Apply();

    private void SaveClicked(object sender, RoutedEventArgs e)
    {
        if (Apply()) Close();
    }

    private void CancelClicked(object sender, RoutedEventArgs e) => Close();

    private void ResetClicked(object sender, RoutedEventArgs e)
    {
        HideMessages();
        var defaults = new AppSettings();
        Display(defaults);
        if (startupEnabled.HasValue) StartupBox.IsChecked = false;
        forgetRemembered = true;
        ShowStatus("Defaults restored on this page. Press Apply or Save to keep them.");
    }

    private void AlertsClicked(object sender, RoutedEventArgs e) => openAlerts();

    private void OpenDataFolderClicked(object sender, RoutedEventArgs e)
    {
        HideMessages();
        try { openDataFolder(); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or Win32Exception or InvalidOperationException)
        {
            Diagnostics.Warning("settings_window", "open_data_folder_failed", exception);
            ShowError("The data folder could not be opened.");
        }
    }

    private void ShowError(string message) { StatusText.Visibility = Visibility.Collapsed; ErrorText.Text = message; ErrorText.Visibility = Visibility.Visible; }
    private void ShowStatus(string message) { ErrorText.Visibility = Visibility.Collapsed; StatusText.Text = message; StatusText.Visibility = Visibility.Visible; }
    private void HideMessages() { ErrorText.Visibility = Visibility.Collapsed; StatusText.Visibility = Visibility.Collapsed; }
}
