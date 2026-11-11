using System;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using CopilotUsageMeter.Contracts;
using CopilotUsageMeter.Panel.Core;

namespace CopilotUsageMeter.Panel.Vsix
{
    // One-line usage row plus an anchored popup. All figures come from UsageDisplay; this class only draws them.
    internal sealed class CompactUsageView : UserControl
    {
        private static readonly Brush Blue = Solid(0x2F, 0x6F, 0xDE);
        private static readonly Brush Orange = Solid(0xE8, 0x89, 0x0C);
        private static readonly Brush Red = Solid(0xD1, 0x34, 0x38);
        private static readonly Brush LightBlue = Solid(0x8F, 0xB4, 0xF0);
        private static readonly Brush Grey = Solid(0x8A, 0x8A, 0x8A);
        private static readonly Brush Track = new SolidColorBrush(Color.FromArgb(0x40, 0x80, 0x80, 0x80));
        private static readonly Brush Hover = new SolidColorBrush(Color.FromArgb(0x30, 0x80, 0x80, 0x80));
        private static readonly Brush FocusRing = Solid(0x80, 0x80, 0x80);
        private static readonly Brush TickEdge = new SolidColorBrush(Color.FromArgb(0x70, 0xFF, 0xFF, 0xFF));
        private static readonly Brush HoverEdge = Solid(0xFF, 0xFF, 0xFF);
        private static readonly FontFamily IconFont = new FontFamily("Segoe MDL2 Assets");
        private const double RowGap = 8, ChevronSize = 9;
        private const int TooltipDelayMs = 150;
        // Estimated widths of the fixed row parts, including chevron and menu; the bar strip gets whatever remains.
        private const double WideFixedWidth = 566, MediumFixedWidth = 337;

        private enum Tier { Wide, Medium, Narrow }

        private readonly Action<ProtocolMessage> send;
        private readonly Grid root = new Grid { Margin = new Thickness(4, 2, 4, 2) };
        private readonly StackPanel content = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        private readonly Button chevron;
        private readonly Button menu;
        private readonly Popup popup;
        private readonly Border popupHost = new Border { BorderThickness = new Thickness(1), Focusable = true };
        private UsageDisplay display = UsageDisplay.Create(null, false, DateTimeOffset.UtcNow);
        private bool connected;
        private string? placementNote;
        private bool confirmingLogOut;
        private string signature = "";
        private DateTime closedAt = DateTime.MinValue;
        private Button? cancelButton;
        private bool busy;
        private readonly ProgressBar rowProgress = new ProgressBar
        {
            Height = 2, VerticalAlignment = VerticalAlignment.Bottom, BorderThickness = new Thickness(0), Background = Brushes.Transparent,
            Foreground = Blue, IsHitTestVisible = false, Visibility = Visibility.Collapsed
        };

        public CompactUsageView(Action<ProtocolMessage> send)
        {
            this.send = send;
            Name = "CopilotUsageMeterPanel";
            SetResourceReference(BackgroundProperty, Microsoft.VisualStudio.PlatformUI.EnvironmentColors.ToolWindowBackgroundBrushKey);
            SetResourceReference(ForegroundProperty, Microsoft.VisualStudio.PlatformUI.EnvironmentColors.ToolWindowTextBrushKey);
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            chevron = IconButton("\uE70D", "Show Copilot usage details", "Show details", ChevronSize, 4);
            chevron.Click += (_, _) => TogglePopup();
            chevron.PreviewKeyDown += CloseOnEscape;
            menu = IconButton("\uE712", "Copilot usage actions", "Actions");
            menu.Click += (_, _) => OpenMenu();
            Grid.SetColumn(content, 1);
            Grid.SetColumn(menu, 2);
            content.ClipToBounds = true;
            root.Children.Add(chevron);
            root.Children.Add(content);
            root.Children.Add(menu);
            Grid.SetColumnSpan(rowProgress, 3);
            AutomationProperties.SetName(rowProgress, "Refreshing Copilot usage");
            root.Children.Add(rowProgress);
            popupHost.SetResourceReference(Border.BackgroundProperty, Microsoft.VisualStudio.PlatformUI.EnvironmentColors.ToolWindowBackgroundBrushKey);
            popupHost.SetResourceReference(Border.BorderBrushProperty, Microsoft.VisualStudio.PlatformUI.EnvironmentColors.ToolWindowBorderBrushKey);
            popupHost.PreviewKeyDown += CloseOnEscape;
            popup = new Popup
            {
                Child = popupHost, PlacementTarget = this, Placement = PlacementMode.Bottom,
                StaysOpen = false, AllowsTransparency = false
            };
            popup.Closed += (_, _) =>
            {
                closedAt = DateTime.UtcNow;
                confirmingLogOut = false;
                chevron.Content = Icon("\uE70D", ChevronSize);
                signature = "";
            };
            root.Children.Add(popup);
            SizeChanged += (_, _) => Rebuild(force: false);
            Content = root;
            Rebuild(force: true);
        }

        public void Update(UsageSnapshot? snapshot, bool connected, string connectionStatus, string? placementNote, bool busy = false)
        {
            this.connected = connected;
            this.busy = busy && connected;
            this.placementNote = placementNote;
            display = UsageDisplay.Create(snapshot, connected, DateTimeOffset.UtcNow);
            Rebuild(force: false);
        }

        private Tier CurrentTier() => ActualWidth >= 560 ? Tier.Wide : ActualWidth >= 330 ? Tier.Medium : Tier.Narrow;

        private void Rebuild(bool force)
        {
            var tier = CurrentTier();
            var barCount = BarCountForTier(tier);
            var next = Signature(tier, barCount) + "|" + BarSlot(tier, barCount);
            if (!force && next == signature) return;
            signature = next;
            Opacity = connected ? 1 : 0.6;
            content.Opacity = busy ? 0.6 : 1;
            rowProgress.IsIndeterminate = busy;
            rowProgress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
            AutomationProperties.SetName(this, display.AccessibleSummary);
            menu.IsEnabled = connected;
            BuildRow(tier, barCount);
            if (popup.IsOpen) BuildPopup();
        }

        private string Signature(Tier tier, int barCount) => string.Join("|", new object?[]
        {
            tier, barCount, connected, busy, display.Level, display.IsStale, display.IsPaused, display.HasChat,
            display.UsedFraction?.ToString("0.###"), display.PaceFraction?.ToString("0.###"), display.GitHubHeadline, display.PercentText,
            display.PaceText, display.ScheduleText, display.StateNote, display.LocalTotalText, display.LocalNote, display.ChatText,
            display.BarsSummary, display.LastText, placementNote, confirmingLogOut, popup.IsOpen,
            string.Join(",", display.Segments.Select(s => s.Text)), string.Join(",", display.Bars.Select(b => b.Tooltip + b.Fraction.ToString("0.##")))
        });

        private int BarCountForTier(Tier tier)
        {
            if (tier == Tier.Narrow || !display.HasChat) return 0;
            return UsageDisplay.BarCountFor(BarSpace(tier), display.Bars.Length);
        }

        private double BarSpace(Tier tier) => ActualWidth - 8 - (tier == Tier.Wide ? WideFixedWidth : MediumFixedWidth);

        // The small margin keeps the wider bars from crowding the trailing label or the menu button.
        private double BarSlot(Tier tier, int barCount) => barCount == 0 ? UsageDisplay.BarSlotWidth : UsageDisplay.BarSlotFor(BarSpace(tier) - 12, barCount);

        private void BuildRow(Tier tier, int barCount)
        {
            content.Children.Clear();
            var level = display.Level;
            if (level == GitHubLevel.SignedOut)
            {
                content.Children.Add(Label("Not signed in", bold: true));
                content.Children.Add(LinkButton("Sign in", () => send(new ProtocolMessage { Type = "SignIn" })));
                content.Children.Add(RefreshButton(11, 3, new Thickness(RowGap, 0, 0, 0)));
            }
            else
            {
                var meter = Meter(display, tier == Tier.Wide ? 80 : tier == Tier.Medium ? 70 : 60, 8);
                meter.Margin = new Thickness(0, 0, 4, 0);
                content.Children.Add(meter);
                content.Children.Add(RefreshButton(11, 3, new Thickness(0, 0, RowGap, 0)));
                if (level == GitHubLevel.Over) content.Children.Add(Badge());
                var hasFigures = display.UsedFraction.HasValue;
                var headline = hasFigures && tier == Tier.Narrow ? display.GitHubHeadline.Split('/')[0].Trim() : display.GitHubHeadline;
                var text = Label(headline, bold: true);
                text.ToolTip = display.GitHubSummary + (display.StateNote.Length > 0 ? " · " + display.StateNote : "");
                content.Children.Add(text);
            }
            if (!connected)
            {
                content.Children.Add(Label("Usage host offline", muted: true, margin: new Thickness(RowGap, 0, 0, 0)));
                return;
            }
            if (display.IsStale) content.Children.Add(Label(display.StateNote, muted: true, margin: new Thickness(RowGap, 0, 0, 0)));
            if (tier == Tier.Wide)
            {
                var pill = Pill(display, 80, 8);
                pill.Margin = new Thickness(RowGap * 2, 0, 6, 0);
                content.Children.Add(pill);
                content.Children.Add(Label(display.LocalAvailable
                    ? (display.HasChat ? display.ChatText + " · " : "") + "Local " + display.LocalTotalText.Replace(" total", "")
                    : "Local unavailable"));
            }
            else if (display.HasChat)
            {
                content.Children.Add(new System.Windows.Shapes.Ellipse { Width = 8, Height = 8, Fill = Red, Margin = new Thickness(RowGap * 2, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center });
                content.Children.Add(Label(display.ChatText));
            }
            if (barCount > 0)
            {
                var visible = display.Bars.Skip(display.Bars.Length - barCount).ToArray();
                var strip = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Height = 26, Margin = new Thickness(RowGap * 2, 0, 6, 0) };
                // A half-pixel gap when slots are tight, 1 px otherwise.
                var slot = BarSlot(tier, barCount);
                var gap = slot <= 8 ? 0.5 : 1;
                for (var index = 0; index < visible.Length; index++) strip.Children.Add(BarButton(visible[index], index, visible.Length, slot - gap, 24, gap));
                content.Children.Add(strip);
                if (tier == Tier.Wide) content.Children.Add(Label(display.LastText, muted: true));
            }
        }

        private void TogglePopup()
        {
            if (popup.IsOpen) { popup.IsOpen = false; return; }
            // A click on the chevron that light-dismissed the popup must not immediately reopen it.
            if ((DateTime.UtcNow - closedAt).TotalMilliseconds < 250) return;
            ShowPopup();
        }

        private void ShowPopup()
        {
            BuildPopup();
            popup.IsOpen = true;
            chevron.Content = Icon("\uE70E", ChevronSize);
            signature = "";
        }

        private void CloseOnEscape(object sender, KeyEventArgs args)
        {
            if (args.Key != Key.Escape || !popup.IsOpen) return;
            popup.IsOpen = false;
            args.Handled = true;
        }

        private void OpenMenu()
        {
            var contextMenu = new ContextMenu { PlacementTarget = menu, Placement = PlacementMode.Bottom };
            contextMenu.Items.Add(MakeMenuItem("Refresh now", () => send(new ProtocolMessage { Type = "Refresh" }), connected));
            contextMenu.Items.Add(MakeMenuItem(display.IsPaused ? "Resume updates" : "Pause updates", () => send(new ProtocolMessage { Type = "TogglePause" }), connected));
            contextMenu.Items.Add(MakeMenuItem(display.Level == GitHubLevel.SignedOut ? "Sign in to GitHub" : "Switch GitHub account", () => send(new ProtocolMessage { Type = "SignIn" }), connected));
            contextMenu.Items.Add(MakeMenuItem("Log out of GitHub…", () => { confirmingLogOut = true; ShowPopup(); }, connected && display.Level != GitHubLevel.SignedOut));
            contextMenu.Items.Add(new Separator());
            contextMenu.Items.Add(MakeMenuItem("Full details…", () => send(new ProtocolMessage { Type = "OpenStatus" }), connected));
            contextMenu.IsOpen = true;
        }

        private static MenuItem MakeMenuItem(string header, Action action, bool enabled)
        {
            var item = new MenuItem { Header = header, IsEnabled = enabled };
            item.Click += (_, _) => action();
            return item;
        }

        private void BuildPopup()
        {
            var hadFocus = popupHost.IsKeyboardFocusWithin;
            cancelButton = null;
            var width = Math.Max(200, Math.Min(420, ActualWidth));
            var stack = new StackPanel { Width = width, Margin = new Thickness(12) };
            // Reserved height, so the popup does not jump when a refresh starts or ends.
            stack.Children.Add(new ProgressBar
            {
                Height = 3, Margin = new Thickness(0, 0, 0, 6), BorderThickness = new Thickness(0), Background = Brushes.Transparent, Foreground = Blue,
                IsIndeterminate = busy, Visibility = busy ? Visibility.Visible : Visibility.Hidden
            });
            if (!connected)
            {
                stack.Children.Add(Label("USAGE HOST OFFLINE", bold: true, size: 11, foreground: Red));
                stack.Children.Add(Label("Copilot Usage Meter is not running, so figures are not updating. Start it from the Windows tray or Start menu. Last known figures are shown dimmed.", muted: true, wrap: true, margin: new Thickness(0, 2, 0, 8)));
            }
            var body = new StackPanel { Opacity = connected ? 1 : 0.55 };
            AddGitHub(body, width - 24);
            AddLocal(body);
            AddChart(body, width - 24);
            stack.Children.Add(body);
            if (placementNote != null)
                stack.Children.Add(Label(placementNote, muted: true, wrap: true, size: 11, margin: new Thickness(0, 8, 0, 0)));
            stack.Children.Add(confirmingLogOut ? LogOutConfirm() : Toolbar());
            popupHost.Child = new ScrollViewer
            {
                Content = stack, MaxHeight = 480, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            // The rebuilt content replaces the focused control; keep keyboard users (and Esc) inside the popup.
            if (hadFocus) (cancelButton ?? (UIElement)popupHost).Focus();
        }

        private void AddGitHub(StackPanel host, double width)
        {
            host.Children.Add(WithRefresh(Label(display.GitHubTitle, muted: true, bold: true, size: 11), display.Level == GitHubLevel.SignedOut));
            if (display.Level == GitHubLevel.SignedOut)
            {
                host.Children.Add(Label("Not signed in", bold: true, margin: new Thickness(0, 4, 0, 0)));
                host.Children.Add(Label("Sign in to see account credits. Local tracking works without signing in.", muted: true, wrap: true));
                return;
            }
            var meter = Meter(display, width, 10);
            meter.Margin = new Thickness(0, 6, 0, 4);
            host.Children.Add(meter);
            var hasSchedule = display.ScheduleText.Length > 0;
            host.Children.Add(WithRefresh(Label(display.GitHubSummary, bold: true, wrap: true), !hasSchedule));
            if (display.PaceText.Length > 0) host.Children.Add(Label(display.PaceText, muted: true));
            if (hasSchedule) host.Children.Add(WithRefresh(Label(display.ScheduleText, muted: true, wrap: true), true));
            if (display.StateNote.Length > 0 && connected)
            {
                var color = display.Level == GitHubLevel.Over ? Red : display.IsStale || display.Level == GitHubLevel.Warning ? Orange : null;
                host.Children.Add(Label(display.StateNote, bold: true, wrap: true, foreground: color));
            }
        }

        private void AddLocal(StackPanel host)
        {
            host.Children.Add(Label(display.LocalTitle, muted: true, bold: true, size: 11, margin: new Thickness(0, 12, 0, 0)));
            if (!display.LocalAvailable)
            {
                host.Children.Add(Label("unavailable", bold: true));
                host.Children.Add(Label(display.LocalNote, muted: true, wrap: true));
                return;
            }
            var pill = Pill(display, 0, 10);
            pill.Margin = new Thickness(0, 6, 0, 4);
            host.Children.Add(pill);
            host.Children.Add(Label(display.LocalTotalText, bold: true));
            var chips = new WrapPanel { Margin = new Thickness(0, 2, 0, 2) };
            foreach (var segment in display.Segments) chips.Children.Add(Chip(segment));
            if (!display.HasChat) chips.Children.Add(Label("No active chat", muted: true, margin: new Thickness(0, 0, 10, 0)));
            host.Children.Add(chips);
            host.Children.Add(Label(display.LocalNote, muted: true, wrap: true));
        }

        private void AddChart(StackPanel host, double availableWidth)
        {
            if (!display.HasChat)
            {
                host.Children.Add(Label("No active chat. The turn chart appears when a chat is active.", muted: true, wrap: true, margin: new Thickness(0, 12, 0, 0)));
                return;
            }
            host.Children.Add(Label(display.BarsCaption, muted: true, bold: true, size: 11, margin: new Thickness(0, 12, 0, 0)));
            var strip = new StackPanel { Orientation = Orientation.Horizontal, Height = 44, Margin = new Thickness(0, 4, 0, 4), HorizontalAlignment = HorizontalAlignment.Left };
            for (var index = 0; index < display.Bars.Length; index++) strip.Children.Add(BarButton(display.Bars[index], index, display.Bars.Length, Math.Max(3, Math.Min(12, Math.Floor(availableWidth / Math.Max(1, display.Bars.Length)) - 1)), 42, 1));
            host.Children.Add(strip);
            if (display.BarsSummary.Length > 0) host.Children.Add(Label(display.BarsSummary, muted: true, wrap: true));
        }

        private FrameworkElement Toolbar()
        {
            var bar = new Grid { Margin = new Thickness(0, 10, 0, 0) };
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var icons = new StackPanel { Orientation = Orientation.Horizontal };
            icons.Children.Add(ToolButton(display.IsPaused ? "\uE768" : "\uE769", display.IsPaused ? "Resume updates" : "Pause updates", () => send(new ProtocolMessage { Type = "TogglePause" })));
            icons.Children.Add(ToolButton("\uE77B", display.Level == GitHubLevel.SignedOut ? "Sign in to GitHub" : "Switch GitHub account", () => send(new ProtocolMessage { Type = "SignIn" })));
            var logout = ToolButton("\uE7E8", "Log out of GitHub", () => { confirmingLogOut = true; BuildPopup(); });
            logout.IsEnabled = connected && display.Level != GitHubLevel.SignedOut;
            icons.Children.Add(logout);
            var full = LinkButton("Full details…", () => { popup.IsOpen = false; send(new ProtocolMessage { Type = "OpenStatus" }); });
            full.HorizontalAlignment = HorizontalAlignment.Right;
            full.IsEnabled = connected;
            Grid.SetColumn(full, 1);
            bar.Children.Add(icons);
            bar.Children.Add(full);
            return bar;
        }

        private FrameworkElement LogOutConfirm()
        {
            var panel = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
            panel.Children.Add(Label("Log out of GitHub?", bold: true));
            panel.Children.Add(Label("Local tracking continues. You can sign in again anytime.", muted: true, wrap: true));
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
            cancelButton = TextButton("Cancel", null, () => { confirmingLogOut = false; BuildPopup(); });
            buttons.Children.Add(cancelButton);
            buttons.Children.Add(TextButton("Log out", Red, () => { confirmingLogOut = false; popup.IsOpen = false; send(new ProtocolMessage { Type = "SignOut" }); }));
            panel.Children.Add(buttons);
            return panel;
        }

        private static FrameworkElement Meter(UsageDisplay display, double width, double height)
        {
            var grid = new Grid { Height = height, VerticalAlignment = VerticalAlignment.Center, Background = Track };
            if (width > 0) grid.Width = width; else grid.HorizontalAlignment = HorizontalAlignment.Stretch;
            AutomationProperties.SetName(grid, "GitHub credits " + display.GitHubSummary);
            if (display.UsedFraction.HasValue)
            {
                var color = !display.IsConnected ? Grey : display.Level == GitHubLevel.Over ? Red : display.Level == GitHubLevel.Warning ? Orange : Blue;
                var used = new Border { Background = color, Opacity = display.IsStale || !display.IsConnected ? 0.55 : 1, ToolTip = "Used this month: " + display.GitHubSummary };
                grid.Children.Add(SplitColumns(display.UsedFraction.Value, used, HorizontalAlignment.Stretch));
                Highlight(used);
                if (display.PaceFraction.HasValue)
                {
                    // Black marker with a faint light edge stays visible on dark themes and on blue, orange and red fills.
                    var tick = new Border { Width = 5, Background = Brushes.Black, BorderBrush = TickEdge, BorderThickness = new Thickness(1) };
                    // A wider transparent hit area makes the thin marker easy to hover.
                    var hit = new Border { Width = 11, Background = Brushes.Transparent, Child = tick, ToolTip = display.PaceText + " (marker shows how far through the month you are)" };
                    grid.Children.Add(SplitColumns(display.PaceFraction.Value, hit, HorizontalAlignment.Right));
                    hit.Margin = new Thickness(0, 0, -5.5, 0);
                    ToolTipService.SetInitialShowDelay(hit, TooltipDelayMs);
                    hit.MouseEnter += (_, _) => tick.BorderBrush = HoverEdge;
                    hit.MouseLeave += (_, _) => tick.BorderBrush = TickEdge;
                }
            }
            else
            {
                grid.Background = Brushes.Transparent;
                grid.Children.Add(new Border { BorderBrush = Grey, BorderThickness = new Thickness(1) });
            }
            return grid;
        }

        private static Grid SplitColumns(double fraction, FrameworkElement child, HorizontalAlignment alignment)
        {
            var split = new Grid();
            split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(fraction, 0.0001), GridUnitType.Star) });
            split.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(1 - fraction, 0), GridUnitType.Star) });
            child.HorizontalAlignment = alignment;
            split.Children.Add(child);
            return split;
        }

        private static FrameworkElement Pill(UsageDisplay display, double width, double height)
        {
            var grid = new Grid { Height = height, VerticalAlignment = VerticalAlignment.Center, Background = Track };
            if (width > 0) grid.Width = width; else grid.HorizontalAlignment = HorizontalAlignment.Stretch;
            var summary = display.LocalAvailable ? display.LocalTotalText + ": " + string.Join(", ", display.Segments.Select(s => s.Label + " " + s.Text)) : "Local usage unavailable";
            AutomationProperties.SetName(grid, "Local use " + summary);
            grid.ToolTip = summary;
            var index = 0;
            foreach (var segment in display.Segments)
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength((double)segment.Credits, GridUnitType.Star) });
                var border = new Border { Background = SegmentBrush(segment.Kind), Opacity = display.IsConnected ? 1 : 0.55, ToolTip = segment.Label + ": " + segment.Text + " credits (local, this machine)" };
                Grid.SetColumn(border, index++);
                Highlight(border);
                grid.Children.Add(border);
            }
            return grid;
        }

        private static Brush SegmentBrush(LocalSegmentKind kind) => kind == LocalSegmentKind.Chat ? Red : kind == LocalSegmentKind.RepositoryRest ? Orange : LightBlue;

        // A light outline on hover tells the user that a tooltip is available for this part of the bar.
        private static void Highlight(Border target)
        {
            ToolTipService.SetInitialShowDelay(target, TooltipDelayMs);
            target.MouseEnter += (_, _) => { target.BorderBrush = HoverEdge; target.BorderThickness = new Thickness(1.5); };
            target.MouseLeave += (_, _) => { target.BorderBrush = null; target.BorderThickness = new Thickness(0); };
        }

        private static FrameworkElement Chip(LocalSegment segment)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 10, 0) };
            panel.Children.Add(new System.Windows.Shapes.Ellipse { Width = 8, Height = 8, Fill = SegmentBrush(segment.Kind), Margin = new Thickness(0, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center });
            panel.Children.Add(Label(segment.Label + " " + segment.Text));
            return panel;
        }

        private static FrameworkElement Badge()
        {
            var badge = new Border
            {
                Width = 12, Height = 12, CornerRadius = new CornerRadius(6), Background = Red, Margin = new Thickness(0, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock { Text = "!", Foreground = Brushes.White, FontSize = 9, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center }
            };
            AutomationProperties.SetName(badge, "Over the allowance");
            return badge;
        }

        private static Button BarButton(DisplayBar bar, int index, int count, double width, double maxHeight, double gap = 2)
        {
            var button = new Button
            {
                Width = width, Height = UsageDisplay.BarHeightFor(bar.Fraction, maxHeight, 4), Margin = new Thickness(gap / 2, 0, gap / 2, 0), Padding = new Thickness(0),
                VerticalAlignment = VerticalAlignment.Bottom, Background = Red, BorderThickness = new Thickness(1), BorderBrush = Brushes.Transparent,
                Opacity = bar.IsNewest ? 1 : 0.5 + 0.35 * (count > 1 ? (double)index / (count - 1) : 1),
                Template = FlatTemplate(hover: false, cornerRadius: 2)
            };
            AutomationProperties.SetName(button, bar.Tooltip);
            var tooltip = new ToolTip { Content = bar.Tooltip };
            button.ToolTip = tooltip;
            ToolTipService.SetInitialShowDelay(button, TooltipDelayMs);
            var restingOpacity = button.Opacity;
            button.MouseEnter += (_, _) => { button.Opacity = 1; button.BorderBrush = HoverEdge; };
            button.MouseLeave += (_, _) => { button.Opacity = restingOpacity; button.BorderBrush = Brushes.Transparent; };
            button.GotKeyboardFocus += (_, _) => tooltip.IsOpen = true;
            button.LostKeyboardFocus += (_, _) => tooltip.IsOpen = false;
            return button;
        }

        private Button ToolButton(string glyph, string name, Action click)
        {
            var button = IconButton(glyph, name, name);
            button.Click += (_, _) => click();
            button.IsEnabled = connected;
            return button;
        }

        private Button RefreshButton(double size, double padding, Thickness margin)
        {
            var button = IconButton("\uE72C", "Refresh now", "Refresh now", size, padding);
            button.Margin = margin;
            button.VerticalAlignment = VerticalAlignment.Center;
            button.Click += (_, _) => send(new ProtocolMessage { Type = "Refresh" });
            button.IsEnabled = connected;
            return button;
        }

        // Text on the left, a small Refresh icon pinned to the right of the same line.
        private FrameworkElement WithRefresh(FrameworkElement text, bool addRefresh)
        {
            if (!addRefresh) return text;
            var line = new Grid();
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var refresh = RefreshButton(11, 3, new Thickness(6, 0, 0, 0));
            Grid.SetColumn(refresh, 1);
            line.Children.Add(text);
            line.Children.Add(refresh);
            return line;
        }

        private static Button IconButton(string glyph, string name, string tooltip, double size = 14, double padding = 6)
        {
            var button = new Button
            {
                Content = Icon(glyph, size), Padding = new Thickness(padding), Margin = new Thickness(0, 0, 2, 0), Background = Brushes.Transparent,
                BorderThickness = new Thickness(1), BorderBrush = Brushes.Transparent, ToolTip = tooltip, Template = FlatTemplate(hover: true)
            };
            AutomationProperties.SetName(button, name);
            return button;
        }

        private static TextBlock Icon(string glyph, double size = 14) => new TextBlock { Text = glyph, FontFamily = IconFont, FontSize = size, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };

        private static Button LinkButton(string text, Action click)
        {
            var button = new Button
            {
                Content = new TextBlock { Text = text, TextDecorations = TextDecorations.Underline, Foreground = Blue }, Padding = new Thickness(4, 2, 4, 2),
                Margin = new Thickness(4, 0, 0, 0), Background = Brushes.Transparent, BorderThickness = new Thickness(1), BorderBrush = Brushes.Transparent,
                Cursor = Cursors.Hand, Template = FlatTemplate(hover: false)
            };
            AutomationProperties.SetName(button, text.TrimEnd('…'));
            button.Click += (_, _) => click();
            return button;
        }

        private static Button TextButton(string text, Brush? background, Action click)
        {
            var label = new TextBlock { Text = text };
            var button = new Button { Content = label, Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 0, 8, 0), BorderThickness = new Thickness(1), BorderBrush = Grey };
            if (background != null)
            {
                button.Background = background;
                label.Foreground = Brushes.White;
            }
            AutomationProperties.SetName(button, text);
            button.Click += (_, _) => click();
            return button;
        }

        private static ControlTemplate FlatTemplate(bool hover, double cornerRadius = 0)
        {
            var border = new FrameworkElementFactory(typeof(Border)) { Name = "bd" };
            if (cornerRadius > 0) border.SetValue(Border.CornerRadiusProperty, new CornerRadius(cornerRadius));
            border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(BackgroundProperty));
            border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(BorderBrushProperty));
            border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(BorderThicknessProperty));
            var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(FrameworkElement.MarginProperty, new TemplateBindingExtension(PaddingProperty));
            presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            border.AppendChild(presenter);
            var template = new ControlTemplate(typeof(Button)) { VisualTree = border };
            var focus = new Trigger { Property = UIElement.IsKeyboardFocusedProperty, Value = true };
            focus.Setters.Add(new Setter(Border.BorderBrushProperty, FocusRing, "bd"));
            template.Triggers.Add(focus);
            if (hover)
            {
                var over = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
                over.Setters.Add(new Setter(Border.BackgroundProperty, Hover, "bd"));
                template.Triggers.Add(over);
            }
            var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
            disabled.Setters.Add(new Setter(UIElement.OpacityProperty, 0.4, "bd"));
            template.Triggers.Add(disabled);
            return template;
        }

        private static TextBlock Label(string text, bool muted = false, bool bold = false, bool wrap = false, double size = 12, Thickness? margin = null, Brush? foreground = null)
        {
            var block = new TextBlock
            {
                Text = text, FontSize = size, VerticalAlignment = VerticalAlignment.Center, Margin = margin ?? new Thickness(0, 1, 0, 1),
                FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, TextWrapping = wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
                TextTrimming = wrap ? TextTrimming.None : TextTrimming.CharacterEllipsis, Opacity = muted ? 0.7 : 1
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
    }
}
