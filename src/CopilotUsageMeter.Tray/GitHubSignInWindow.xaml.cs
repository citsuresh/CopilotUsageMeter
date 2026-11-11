using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using CopilotUsageMeter.Readers.GitHub;

namespace CopilotUsageMeter.Tray;

// Shows the GitHub one-time code and the sign-in progress that used to appear in a console window.
public sealed partial class GitHubSignInWindow : Window, ISignInDialog
{
    private enum Stage { Preparing, WaitingForUser, Connecting, Finished }

    private readonly DispatcherTimer closeTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private Stage stage = Stage.Preparing;
    private bool closed;

    public GitHubSignInWindow()
    {
        InitializeComponent();
        MessageText.Text = "Starting GitHub sign-in...";
        closeTimer.Tick += (_, _) => Close();
    }

    public event Action? CancelRequested;
    public event Action? RetryRequested;
    public event Action? ChoicesRequested;

    public void ShowCode(string code)
    {
        if (closed) return;
        stage = Stage.WaitingForUser;
        MessageText.Text = "Copy this code, then approve access on github.com.";
        CodeBox.Text = code;
        CodeBox.Visibility = Visibility.Visible;
        CopyButton.Visibility = Visibility.Visible;
        HintText.Text = "Waiting for you to finish in the browser. The code stops working after a few minutes.";
        HintText.Visibility = Visibility.Visible;
        BusyBar.Visibility = Visibility.Visible;
    }

    public void ShowConnecting()
    {
        if (closed) return;
        stage = Stage.Connecting;
        MessageText.Text = "Approved. Connecting your GitHub account...";
        CodeBox.Visibility = Visibility.Collapsed;
        CopyButton.Visibility = Visibility.Collapsed;
        HintText.Visibility = Visibility.Collapsed;
        BusyBar.Visibility = Visibility.Visible;
        CloseButton.Content = "Close";
    }

    void ISignInDialog.ShowFinished(SignInFinish finish, string message)
    {
        if (closed) return;
        stage = Stage.Finished;
        MessageText.Text = message;
        CodeBox.Visibility = Visibility.Collapsed;
        CopyButton.Visibility = Visibility.Collapsed;
        HintText.Visibility = Visibility.Collapsed;
        BusyBar.Visibility = Visibility.Collapsed;
        RetryButton.Visibility = finish == SignInFinish.Retry ? Visibility.Visible : Visibility.Collapsed;
        ChooseButton.Visibility = finish == SignInFinish.ChooseAccount ? Visibility.Visible : Visibility.Collapsed;
        CloseButton.Content = "Close";
        Topmost = false;
        if (finish == SignInFinish.Success) closeTimer.Start();
    }

    public void Bring()
    {
        if (!closed) ForegroundWindow.Bring(this);
    }

    void ISignInDialog.Close()
    {
        if (!closed) Close();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (e.Cancel) return;
        closed = true;
        closeTimer.Stop();
        // Leaving the window while sign-in is still waiting for the user abandons that sign-in.
        var abandoned = stage is Stage.Preparing or Stage.WaitingForUser;
        stage = Stage.Finished;
        if (abandoned) CancelRequested?.Invoke();
    }

    private void CopyClicked(object sender, RoutedEventArgs e)
    {
        var copied = false;
        var opened = false;
        try
        {
            Clipboard.SetText(CodeBox.Text);
            copied = true;
        }
        catch (ExternalException) { }

        try
        {
            Process.Start(new ProcessStartInfo(GitHubDeviceCode.VerificationUrl) { UseShellExecute = true });
            opened = true;
        }
        catch (Win32Exception) { }

        HintText.Text = (copied, opened) switch
        {
            (true, true) => "Code copied. Paste it on the GitHub page, then approve access.",
            (true, false) => "Code copied. Open github.com/login/device in your browser and paste it.",
            (false, true) => "The code could not be copied automatically. Select it above and copy it, then paste it on the GitHub page.",
            _ => "The code could not be copied and the browser could not be opened. Go to github.com/login/device and type the code above."
        };
    }

    private void RetryClicked(object sender, RoutedEventArgs e) => RetryRequested?.Invoke();

    private void ChooseClicked(object sender, RoutedEventArgs e) => ChoicesRequested?.Invoke();

    private void CloseClicked(object sender, RoutedEventArgs e) => Close();
}
