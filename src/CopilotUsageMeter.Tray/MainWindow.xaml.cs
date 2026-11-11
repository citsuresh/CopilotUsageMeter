using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using CopilotUsageMeter.Contracts;
using CopilotUsageMeter.Panel.Core;

namespace CopilotUsageMeter.Tray;

// Full details window. All text and rules come from UsageDisplay; this class only draws them.
public partial class MainWindow : Window
{
    private static readonly Brush Blue = Solid(0x2F, 0x6F, 0xDE);
    private static readonly Brush Orange = Solid(0xE8, 0x89, 0x0C);
    private static readonly Brush Red = Solid(0xD1, 0x34, 0x38);
    private static readonly Brush LightBlue = Solid(0x8F, 0xB4, 0xF0);
    private static readonly Brush Grey = Solid(0x8A, 0x8A, 0x8A);
    private static readonly Brush AmberMeter = new SolidColorBrush(Color.FromArgb(0x59, 0xE8, 0x89, 0x0C));
    private static readonly Brush RedMeter = new SolidColorBrush(Color.FromArgb(0x59, 0xD1, 0x34, 0x38));
    private static readonly Brush TickEdge = new SolidColorBrush(Color.FromArgb(0x70, 0xFF, 0xFF, 0xFF));
    private static readonly Brush HoverEdge = Solid(0xFF, 0xFF, 0xFF);
    private static readonly Brush BarHover = Solid(0xB7, 0x1C, 0x1C);
    private const int TooltipDelayMs = 150;
    private static readonly Brush Track = new SolidColorBrush(Color.FromArgb(0x40, 0x80, 0x80, 0x80));

    private readonly Func<UsageSnapshot> getSnapshot;
    private readonly Func<IReadOnlyList<string>> listLogins;
    private bool? chooserOpenedSignedOut;
    private readonly Func<ProtocolMessage, CancellationToken, Task<ProtocolMessage>> send;
    private UsageDisplay display = UsageDisplay.Create(null, true, DateTimeOffset.UtcNow);
    private string signature = "";

    internal MainWindow(Func<UsageSnapshot> getSnapshot, Func<ProtocolMessage, CancellationToken, Task<ProtocolMessage>> send, Func<IReadOnlyList<string>> listLogins)
    {
        this.getSnapshot = getSnapshot;
        this.send = send;
        this.listLogins = listLogins;
        InitializeComponent();
        VersionText.Text = "v" + ProductVersion.Of(typeof(MainWindow).Assembly);
        // Keep the window, including its button row, above the taskbar on short screens.
        var area = SystemParameters.WorkArea;
        MaxHeight = area.Height;
        Height = Math.Min(Height, area.Height - 40);
        Loaded += (_, _) => { if (Top + ActualHeight > area.Bottom) Top = Math.Max(area.Top, area.Bottom - ActualHeight); };
        RefreshSnapshot(getSnapshot());
    }

    internal void SetFault(string message)
    {
        FaultText.Text = message;
        FaultBanner.Visibility = Visibility.Visible;
    }

    internal void RefreshSnapshot(UsageSnapshot snapshot)
    {
        display = UsageDisplay.Create(snapshot, true, DateTimeOffset.UtcNow);
        StatusText.Text = display.IsPaused ? "Paused" : "Running";
        StatusChip.Background = display.IsPaused ? Orange : Blue;
        PauseLabel.Text = display.IsPaused ? "Resume updates" : "Pause updates";
        PauseGlyph.Text = display.IsPaused ? "\uE768" : "\uE769";
        AutomationProperties.SetName(PauseButton, PauseLabel.Text);
        var signedOut = display.Level == GitHubLevel.SignedOut;
        SwitchLabel.Text = signedOut ? "Sign in" : "Switch account";
        AutomationProperties.SetName(SwitchButton, signedOut ? "Sign in to GitHub" : "Switch GitHub account");
        LogOutButton.IsEnabled = !signedOut;
        if (signedOut)
        {
            LogOutConfirm.Visibility = Visibility.Collapsed;
            Toolbar.Visibility = Visibility.Visible;
        }

        // The refresh timer fires every second, so keep the chooser open and close it only when the sign-in state it was opened for has changed.
        if (chooserOpenedSignedOut is bool openedSignedOut && openedSignedOut != signedOut) HideAccountChoices();

        // Rebuilding every second would drop focus and tooltips, so redraw only when something visible changed.
        var next = string.Join("|", new object?[]
        {
            display.Level, display.IsStale, display.Severity, display.ProblemTooltip, display.UsedFraction, display.PaceFraction, display.GitHubTitle, display.GitHubSummary, display.PaceText,
            display.ScheduleText, display.StateNote, display.LocalAvailable, display.LocalTotalText, display.LocalNote, display.ChatText,
            display.BarsSummary, string.Join(",", display.Segments.Select(s => s.Label + s.Text)),
            string.Join(",", display.Bars.Select(b => b.Tooltip + b.Fraction)),
            string.Join(",", display.AccountRows.Concat(display.LocalRows).Concat(display.DiagnosticRows).Select(r => r.Label + r.Value))
        });
        if (next == signature) return;
        signature = next;
        BuildGitHub();
        BuildLocal();
        BuildChart();
        FillRows(AccountRowsHost, display.AccountRows);
        FillRows(LocalRowsHost, display.LocalRows);
        FillRows(DiagnosticRowsHost, display.DiagnosticRows);
    }

    private void BuildGitHub()
    {
        GitHubHost.Children.Clear();
        GitHubHost.Children.Add(Heading(display.GitHubTitle));
        if (display.Level == GitHubLevel.SignedOut)
        {
            if (display.AccountProblem != null) GitHubHost.Children.Add(ProblemBlock(display.AccountProblem, 15));
            else
            {
                GitHubHost.Children.Add(Text("Not signed in", bold: true, margin: new Thickness(0, 4, 0, 0)));
                GitHubHost.Children.Add(Text("Sign in to see account credits. Local tracking works without signing in.", muted: true));
            }
            return;
        }
        GitHubHost.Children.Add(Meter());
        if (display.AccountProblem != null) GitHubHost.Children.Add(ProblemBlock(display.AccountProblem, 15));
        else GitHubHost.Children.Add(Text(display.GitHubSummary, bold: true, size: 15));
        if (display.PaceText.Length > 0) GitHubHost.Children.Add(Text(display.PaceText, muted: true));
        if (display.ScheduleText.Length > 0) GitHubHost.Children.Add(Text(display.ScheduleText, muted: true));
        if (display.StateNote.Length > 0)
        {
            var color = display.Level == GitHubLevel.Over ? Red : display.IsStale || display.Level == GitHubLevel.Warning ? Orange : null;
            GitHubHost.Children.Add(Text(display.StateNote, bold: true, foreground: color));
        }
    }

    private void BuildLocal()
    {
        LocalHost.Children.Clear();
        LocalHost.Children.Add(Heading(display.LocalTitle));
        if (!display.LocalAvailable)
        {
            LocalHost.Children.Add(Text("unavailable", bold: true, margin: new Thickness(0, 4, 0, 0)));
            if (display.LocalProblem != null) LocalHost.Children.Add(ProblemBlock(display.LocalProblem, 12));
            else LocalHost.Children.Add(Text(display.LocalNote, muted: true));
            return;
        }
        var summary = display.LocalTotalText + ": " + string.Join(", ", display.Segments.Select(s => s.Label + " " + s.Text));
        var pill = new Grid { Height = 12, Background = Track, Margin = new Thickness(0, 6, 0, 6) };
        AutomationProperties.SetName(pill, "Local use " + summary);
        pill.ToolTip = summary;
        var column = 0;
        foreach (var segment in display.Segments)
        {
            pill.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength((double)segment.Credits, GridUnitType.Star) });
            var border = new Border { Background = SegmentBrush(segment.Kind), ToolTip = segment.Label + ": " + segment.Text + " credits (local, this machine)" };
            Grid.SetColumn(border, column++);
            Highlight(border);
            pill.Children.Add(border);
        }
        LocalHost.Children.Add(pill);
        LocalHost.Children.Add(Text(display.LocalTotalText, bold: true, size: 15));
        var chips = new WrapPanel { Margin = new Thickness(0, 2, 0, 2) };
        foreach (var segment in display.Segments)
        {
            var chip = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 14, 0) };
            chip.Children.Add(new System.Windows.Shapes.Ellipse { Width = 9, Height = 9, Fill = SegmentBrush(segment.Kind), Margin = new Thickness(0, 0, 5, 0) });
            chip.Children.Add(Text(segment.Label + " " + segment.Text));
            chips.Children.Add(chip);
        }
        if (!display.HasChat) chips.Children.Add(Text("No active chat", muted: true));
        LocalHost.Children.Add(chips);
        LocalHost.Children.Add(Text(display.LocalNote, muted: true));
        if (display.LocalProblem != null) LocalHost.Children.Add(ProblemBlock(display.LocalProblem, 12));
    }

    // Amber for a warning, red for an error: a "!" badge and short title, then the explanation and the error code for support.
    private static FrameworkElement ProblemBlock(DisplayProblem problem, double titleSize)
    {
        var color = problem.Severity == ProblemSeverity.Error ? Red : Orange;
        var panel = new StackPanel { Margin = new Thickness(0, 2, 0, 2) };
        var header = new StackPanel { Orientation = Orientation.Horizontal };
        var badge = new Border
        {
            Width = 16, Height = 16, CornerRadius = new CornerRadius(8), Background = color, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = "!", Foreground = Brushes.White, FontSize = 11, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            ToolTip = problem.Title + "\n" + problem.Detail + (problem.Code != null ? "\nError code: " + problem.Code : "")
        };
        AutomationProperties.SetName(badge, problem.Title);
        header.Children.Add(badge);
        header.Children.Add(Text(problem.Title, bold: true, size: titleSize, foreground: color));
        panel.Children.Add(header);
        panel.Children.Add(Text(problem.Detail, muted: true));
        if (problem.Code != null) panel.Children.Add(Text("Error code: " + problem.Code, muted: true, size: 11));
        return panel;
    }

    private void BuildChart()
    {
        ChartHost.Children.Clear();
        if (!display.HasChat)
        {
            ChartHost.Children.Add(Text("No active chat. The turn chart appears when a chat is active.", muted: true));
            return;
        }
        ChartHost.Children.Add(Heading(display.BarsCaption));
        var strip = new StackPanel { Orientation = Orientation.Horizontal, Height = 64, Margin = new Thickness(0, 6, 0, 6), HorizontalAlignment = HorizontalAlignment.Left };
        for (var index = 0; index < display.Bars.Length; index++)
        {
            var bar = display.Bars[index];
            var opacity = bar.IsNewest ? 1 : 0.35 + 0.5 * (display.Bars.Length > 1 ? (double)index / (display.Bars.Length - 1) : 1);
            var item = new Border { Width = 14, Height = UsageDisplay.BarHeightFor(bar.Fraction, 62, 4), Margin = new Thickness(1, 0, 1, 0), Background = Red, Opacity = opacity, VerticalAlignment = VerticalAlignment.Bottom, Focusable = true, ToolTip = bar.Tooltip };
            AutomationProperties.SetName(item, bar.Tooltip);
            ToolTipService.SetInitialShowDelay(item, TooltipDelayMs);
            item.MouseEnter += (_, _) => { item.Opacity = 1; item.Background = BarHover; };
            item.MouseLeave += (_, _) => { item.Opacity = opacity; item.Background = Red; };
            strip.Children.Add(item);
        }
        ChartHost.Children.Add(strip);
        if (display.BarsSummary.Length > 0) ChartHost.Children.Add(Text(display.BarsSummary, muted: true));
    }

    private FrameworkElement Meter()
    {
        var grid = new Grid { Height = 12, Background = Track, Margin = new Thickness(0, 6, 0, 6) };
        AutomationProperties.SetName(grid, "GitHub credits " + display.GitHubSummary);
        if (!display.UsedFraction.HasValue)
        {
            var problem = display.AccountProblem;
            var error = problem?.Severity == ProblemSeverity.Error;
            if (problem != null) grid.Background = error ? RedMeter : AmberMeter;
            grid.Children.Add(new Border { BorderBrush = problem == null ? Grey : error ? Red : Orange, BorderThickness = new Thickness(1) });
            return grid;
        }
        var color = display.Level == GitHubLevel.Over ? Red : display.Level == GitHubLevel.Warning ? Orange : Blue;
        var used = new Border { Background = color, Opacity = display.IsStale ? 0.55 : 1, ToolTip = "Used this month: " + display.GitHubSummary };
        Highlight(used);
        grid.Children.Add(Split(display.UsedFraction.Value, used, HorizontalAlignment.Stretch));
        if (display.PaceFraction.HasValue)
        {
            // Black marker with a faint light edge; the wider transparent hit area makes it easy to hover.
            var tick = new Border { Width = 5, Background = Brushes.Black, BorderBrush = TickEdge, BorderThickness = new Thickness(1) };
            var hit = new Border { Width = 11, Background = Brushes.Transparent, Child = tick, Margin = new Thickness(0, 0, -5.5, 0), ToolTip = display.PaceText + " (marker shows how far through the month you are)" };
            grid.Children.Add(Split(display.PaceFraction.Value, hit, HorizontalAlignment.Right));
            hit.MouseEnter += (_, _) => tick.BorderBrush = HoverEdge;
            ToolTipService.SetInitialShowDelay(hit, TooltipDelayMs);
            hit.MouseLeave += (_, _) => tick.BorderBrush = TickEdge;
        }
        return grid;
    }

    private static Grid Split(double fraction, FrameworkElement child, HorizontalAlignment alignment)
    {
        var split = new Grid();
        split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(fraction, 0.0001), GridUnitType.Star) });
        split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(1 - fraction, 0), GridUnitType.Star) });
        child.HorizontalAlignment = alignment;
        split.Children.Add(child);
        return split;
    }

    private static Brush SegmentBrush(LocalSegmentKind kind) => kind == LocalSegmentKind.Chat ? Red : kind == LocalSegmentKind.RepositoryRest ? Orange : LightBlue;

    // A light outline on hover tells the user that a tooltip is available for this part of the bar.
    private static void Highlight(Border target)
    {
        ToolTipService.SetInitialShowDelay(target, TooltipDelayMs);
        target.MouseEnter += (_, _) => { target.BorderBrush = HoverEdge; target.BorderThickness = new Thickness(1.5); };
        target.MouseLeave += (_, _) => { target.BorderBrush = null; target.BorderThickness = new Thickness(0); };
    }

    private static void FillRows(StackPanel host, DisplayRow[] rows)
    {
        host.Children.Clear();
        foreach (var row in rows)
        {
            var line = new Grid { Margin = new Thickness(0, 1, 0, 1) };
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var label = Text(row.Label, muted: true);
            var value = Text(row.Value);
            Grid.SetColumn(value, 1);
            line.Children.Add(label);
            line.Children.Add(value);
            AutomationProperties.SetName(line, row.Label + ": " + row.Value);
            host.Children.Add(line);
        }
    }

    private static TextBlock Heading(string text) => Text(text, muted: true, bold: true, size: 11);

    private static TextBlock Text(string text, bool muted = false, bool bold = false, double size = 12, Thickness? margin = null, Brush? foreground = null)
    {
        var block = new TextBlock
        {
            Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap, Margin = margin ?? new Thickness(0, 1, 0, 1),
            FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, Opacity = muted ? 0.7 : 1
        };
        if (foreground != null) block.Foreground = foreground;
        return block;
    }

    private static Brush Solid(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    private void DismissFaultClicked(object sender, RoutedEventArgs e) => FaultBanner.Visibility = Visibility.Collapsed;

    // One button per saved GitHub CLI login (names only), so the user picks instead of typing.
    // Signed in: the connected login is disabled and "Add a new account" is offered. Signed out: shown only when several logins exist.
    internal void ShowAccountChoices()
    {
        var account = getSnapshot().Account;
        var signedOut = account.Status == "signed-out";
        var logins = listLogins();
        AccountChoiceButtons.Children.Clear();
        if (signedOut && logins.Count < 2) { AccountChoices.Visibility = Visibility.Collapsed; chooserOpenedSignedOut = null; return; }
        AccountChoicesTitle.Text = signedOut ? "Choose a GitHub account:" : "Switch to a saved GitHub account, or add a new one:";
        foreach (var login in logins)
        {
            var choice = login;
            var connected = !signedOut && string.Equals(choice, account.Login, StringComparison.OrdinalIgnoreCase);
            var button = new Button
            {
                Content = connected ? choice + " (connected)" : choice,
                IsEnabled = !connected,
                Padding = new Thickness(12, 6, 12, 6),
                Margin = new Thickness(0, 0, 8, 4)
            };
            AutomationProperties.SetName(button, "Connect GitHub account " + choice);
            button.Click += async (_, _) =>
            {
                HideAccountChoices();
                await ExecuteAsync(new ProtocolMessage { Type = "ConnectAccount", AccountLogin = choice });
            };
            AccountChoiceButtons.Children.Add(button);
        }
        var add = new Button { Content = "Add a new account\u2026", Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(0, 0, 8, 4) };
        AutomationProperties.SetName(add, "Add a new GitHub account");
        add.Click += async (_, _) =>
        {
            HideAccountChoices();
            await ExecuteAsync(new ProtocolMessage { Type = "BeginSignIn" });
        };
        AccountChoiceButtons.Children.Add(add);
        chooserOpenedSignedOut = signedOut;
        AccountChoices.Visibility = Visibility.Visible;
    }

    private void HideAccountChoices()
    {
        AccountChoices.Visibility = Visibility.Collapsed;
        chooserOpenedSignedOut = null;
    }

    private async void RefreshClicked(object sender, RoutedEventArgs e) => await ExecuteAsync(new ProtocolMessage { Type = "Refresh" });
    private async void PauseClicked(object sender, RoutedEventArgs e) => await ExecuteAsync(new ProtocolMessage { Type = "SetPaused", Paused = !getSnapshot().IsPaused });
    // Signed out: import an existing secure gh login first (as the panel does) and open the browser sign-in only when there is none. Signed in: list saved accounts plus "Add a new account".
    private async void SignInClicked(object sender, RoutedEventArgs e) => await SwitchOrSignInAsync();
    private async Task SwitchOrSignInAsync()
    {
        if (getSnapshot().Account.Status == "signed-out") await ExecuteAsync(new ProtocolMessage { Type = "SignIn" });
        else ShowAccountChoices();
    }

    private async void SkipClicked(object sender, RoutedEventArgs e) => await ExecuteAsync(new ProtocolMessage { Type = "SkipAccount" });
    private async void ConnectClicked(object sender, RoutedEventArgs e) => await ExecuteAsync(new ProtocolMessage { Type = "ConnectAccount", AccountLogin = AccountLogin.Text.Trim() });

    private void LogOutClicked(object sender, RoutedEventArgs e)
    {
        LogOutConfirm.Visibility = Visibility.Visible;
        Toolbar.Visibility = Visibility.Collapsed;
    }

    private void CancelLogOutClicked(object sender, RoutedEventArgs e)
    {
        LogOutConfirm.Visibility = Visibility.Collapsed;
        Toolbar.Visibility = Visibility.Visible;
    }

    private async void ConfirmLogOutClicked(object sender, RoutedEventArgs e)
    {
        CancelLogOutClicked(sender, e);
        await ExecuteAsync(new ProtocolMessage { Type = "SignOut" });
    }

    private async Task ExecuteAsync(ProtocolMessage command)
    {
        pendingCommands++;
        BusyBar.Visibility = Visibility.Visible;
        try
        {
            var reply = await send(command, CancellationToken.None);
            SetNotice(reply.Message ?? "Command acknowledged.");
        }
        catch (Exception exception) when (exception is IOException || exception is OperationCanceledException)
        {
            SetNotice("Command did not complete; check the tray status before retrying.");
        }
        finally
        {
            if (--pendingCommands == 0) BusyBar.Visibility = Visibility.Collapsed;
        }
    }

    private int pendingCommands;

    // The notice is replaced by each command and clears itself so a stale result is never left on screen.
    private CancellationTokenSource? noticeClear;

    private async void SetNotice(string message)
    {
        noticeClear?.Cancel();
        var token = (noticeClear = new CancellationTokenSource()).Token;
        NoticeText.Text = message;
        NoticeText.Visibility = Visibility.Visible;
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(8), token);
            NoticeText.Visibility = Visibility.Collapsed;
        }
        catch (OperationCanceledException) { }
    }
}
