using System;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using CopilotUsageMeter.Client;
using CopilotUsageMeter.Contracts;
using CopilotUsageMeter.Panel.Core;
using Microsoft.VisualStudio.Shell;

namespace CopilotUsageMeter.Panel.Vsix
{
    [Guid("7983d238-c688-43fa-b741-b1bb26835fc1")]
    public sealed class UsageDemoToolWindow : ToolWindowPane
    {
        private readonly UsagePanelViewModel model;

        public UsageDemoToolWindow() : base(null)
        {
            Caption = "Copilot Usage Meter - Phase 2 Demo";
            using (var identity = WindowsIdentity.GetCurrent())
            {
                var user = identity.User ?? throw new InvalidOperationException("Windows user SID is unavailable.");
                model = new UsagePanelViewModel(new PipeUsageClient(UsageProtocol.PipePrefix + user.Value),
                    action => Application.Current.Dispatcher.BeginInvoke(action));
            }
            var panel = new StackPanel { Margin = new Thickness(16), DataContext = model };
            panel.Children.Add(new TextBlock { Text = "PHASE 2 DEMO - invented data only", FontSize = 18, FontWeight = FontWeights.Bold,
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12) });
            panel.Children.Add(Text("Status"));
            panel.Children.Add(new TextBlock { Text = "Temporary transport test surface, NOT Copilot Chat embedding.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 12) });
            var values = new StackPanel();
            values.SetBinding(UIElement.OpacityProperty, new Binding("DataOpacity"));
            values.Children.Add(new TextBlock { Text = "Demo account - not GitHub-reported usage", FontWeight = FontWeights.Bold });
            values.Children.Add(Text("AccountText"));
            values.Children.Add(new TextBlock { Text = "Demo local analytics - separate from account billing", FontWeight = FontWeights.Bold });
            values.Children.Add(Text("LocalText"));
            values.Children.Add(Text("Identity"));
            panel.Children.Add(values);
            var buttons = new WrapPanel { Margin = new Thickness(0, 8, 0, 8) };
            buttons.SetBinding(UIElement.IsEnabledProperty, new Binding("CanSend"));
            var refresh = new Button { Content = "Refresh demo", Padding = new Thickness(8, 5, 8, 5), Margin = new Thickness(0, 0, 8, 0) };
            refresh.Click += async (_, _) => await model.RefreshAsync();
            buttons.Children.Add(refresh);
            var pause = new Button { Padding = new Thickness(8, 5, 8, 5) };
            pause.SetBinding(ContentControl.ContentProperty, new Binding("PauseLabel"));
            pause.Click += async (_, _) => await model.TogglePauseAsync();
            buttons.Children.Add(pause);
            var status = new Button { Content = "Tray status / settings", Padding = new Thickness(8, 5, 8, 5), Margin = new Thickness(8, 0, 0, 0) };
            status.Click += async (_, _) => await model.OpenStatusAsync();
            buttons.Children.Add(status);
            panel.Children.Add(buttons);
            var settings = new WrapPanel();
            settings.SetBinding(UIElement.IsEnabledProperty, new Binding("CanSend"));
            var interval = new TextBox { Width = 55, Margin = new Thickness(8, 0, 8, 0), VerticalContentAlignment = VerticalAlignment.Center };
            interval.SetBinding(TextBox.TextProperty, new Binding("DemoIntervalText") { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
            settings.Children.Add(new Label { Content = "Demo refresh seconds (5-300):", Target = interval });
            settings.Children.Add(interval);
            var save = new Button { Content = "Save demo settings", Padding = new Thickness(8, 5, 8, 5) };
            save.Click += async (_, _) => await model.SaveSettingsAsync();
            settings.Children.Add(save);
            panel.Children.Add(settings);
            panel.Children.Add(Text("Notice"));
            Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            model.Start();
        }

        private static TextBlock Text(string path)
        {
            var text = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 10) };
            text.SetBinding(TextBlock.TextProperty, new Binding(path));
            return text;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) model.Dispose();
            base.Dispose(disposing);
        }
    }
}