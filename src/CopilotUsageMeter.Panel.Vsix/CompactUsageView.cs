using System;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using CopilotUsageMeter.Contracts;
using CopilotUsageMeter.Panel.Core;

namespace CopilotUsageMeter.Panel.Vsix
{
    internal sealed class CompactUsageView : UserControl
    {
        private readonly TextBlock header = Text();
        private readonly TextBlock account = Text();
        private readonly TextBlock local = Text();
        private readonly TextBlock freshness = Text();
        private readonly TextBlock chat = Text();
        private readonly TextBlock placement = Text();
        private readonly StackPanel bars = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Bottom, Height = 26 };
        private readonly StackPanel details = new StackPanel();
        private readonly ScrollViewer detailScroll;
        private readonly WrapPanel actions = new WrapPanel();
        private readonly ToggleButton expand;
        private readonly Action<ProtocolMessage> send;
        private string chartSignature = "";
        public event Action<bool>? ExpansionChanged;

        public CompactUsageView(Action<ProtocolMessage> send, bool expanded)
        {
            this.send = send;
            Name = "CopilotUsageMeterPanel";
            SetResourceReference(BackgroundProperty, Microsoft.VisualStudio.PlatformUI.EnvironmentColors.ToolWindowBackgroundBrushKey);
            SetResourceReference(ForegroundProperty, Microsoft.VisualStudio.PlatformUI.EnvironmentColors.ToolWindowTextBrushKey);
            var root = new StackPanel { Margin = new Thickness(4) };
            var line = new WrapPanel();
            expand = new ToggleButton { Content = expanded ? "v" : ">", IsChecked = expanded, ToolTip = "Expand usage details", Padding = new Thickness(3), Margin = new Thickness(0, 0, 4, 0) };
            detailScroll = new ScrollViewer
            {
                Content = details, MaxHeight = 240, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Visibility = expanded ? Visibility.Visible : Visibility.Collapsed
            };
            AutomationProperties.SetName(expand, "Expand Copilot usage details");
            RoutedEventHandler expansionChanged = (_, _) =>
            {
                detailScroll.Visibility = expand.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
                expand.Content = expand.IsChecked == true ? "v" : ">";
                ExpansionChanged?.Invoke(expand.IsChecked == true);
            };
            expand.Checked += expansionChanged;
            expand.Unchecked += expansionChanged;
            line.Children.Add(expand);
            line.Children.Add(header);
            line.Children.Add(bars);
            root.Children.Add(line);
            root.Children.Add(placement);
            details.Children.Add(account);
            details.Children.Add(freshness);
            details.Children.Add(local);
            details.Children.Add(chat);
            AddAction("Refresh", "Refresh");
            AddAction("Pause / resume", "TogglePause");
            AddAction("Sign in / switch GitHub account", "SignIn");
            AddAction("Log out", "SignOut");
            AddAction("Settings / details", "OpenStatus");
            details.Children.Add(actions);
            root.Children.Add(detailScroll);
            Content = root;
        }

        public void Update(UsageSnapshot? snapshot, bool connected, string connectionStatus, string placementStatus)
        {
            var presentation = CompactUsagePresentation.Create(snapshot, connected, DateTimeOffset.UtcNow);
            header.Text = presentation.Header;
            header.ToolTip = presentation.Chat;
            account.Text = presentation.Account;
            local.Text = presentation.Local;
            freshness.Text = presentation.Refresh;
            chat.Text = presentation.Chat;
            placement.Text = placementStatus + (connected ? "" : " | " + connectionStatus);
            Opacity = connected ? 1 : 0.5;
            actions.IsEnabled = connected;
            var signature = string.Join("|", presentation.Bars.Select(bar => bar.Tooltip + ":" + bar.Height));
            if (signature == chartSignature) return;
            chartSignature = signature;
            bars.Children.Clear();
            foreach (var bar in presentation.Bars)
            {
                var button = new Button
                {
                    Width = 8, Height = bar.Height, Margin = new Thickness(1, 0, 1, 0), Padding = new Thickness(0),
                    VerticalAlignment = VerticalAlignment.Bottom, ToolTip = bar.Tooltip,
                    Background = bar.IsNewest ? Brushes.DarkOrange : Brushes.SteelBlue
                };
                AutomationProperties.SetName(button, bar.Tooltip);
                ToolTipService.SetShowOnDisabled(button, true);
                var tooltip = new ToolTip { Content = bar.Tooltip };
                button.ToolTip = tooltip;
                button.GotKeyboardFocus += (_, _) => tooltip.IsOpen = true;
                button.LostKeyboardFocus += (_, _) => tooltip.IsOpen = false;
                bars.Children.Add(button);
            }
        }

        private void AddAction(string label, string type)
        {
            var button = new Button { Content = label, Margin = new Thickness(2), Padding = new Thickness(4) };
            button.Click += (_, _) => send(new ProtocolMessage { Type = type });
            actions.Children.Add(button);
        }

        private static TextBlock Text() => new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2) };
    }
}