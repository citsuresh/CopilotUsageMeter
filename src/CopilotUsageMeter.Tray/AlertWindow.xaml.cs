using System.Windows;

namespace CopilotUsageMeter.Tray;

// A small non-modal message that stays above other windows without taking focus from what the user is typing.
public partial class AlertWindow : Window
{
    internal AlertWindow(string title, string body)
    {
        InitializeComponent();
        TitleText.Text = title;
        BodyText.Text = body;
        Loaded += (_, _) =>
        {
            var area = SystemParameters.WorkArea;
            Left = area.Right - ActualWidth - 16;
            Top = area.Bottom - ActualHeight - 16;
        };
    }

    private void DismissClicked(object sender, RoutedEventArgs e) => Close();
}
