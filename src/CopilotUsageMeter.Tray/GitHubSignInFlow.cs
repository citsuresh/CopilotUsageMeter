using System;
using System.Threading.Tasks;
using CopilotUsageMeter.Readers.GitHub;

namespace CopilotUsageMeter.Tray;

internal enum SignInFinish { Success, Retry, ChooseAccount }

// The sign-in window as the flow sees it. Implementations are UI-thread objects; every call is made through the flow's post delegate,
// must be a no-op once the window is closed, and Close must be safe to call repeatedly.
internal interface ISignInDialog
{
    event Action? CancelRequested;
    event Action? RetryRequested;
    event Action? ChoicesRequested;
    void ShowCode(string code);
    void ShowConnecting();
    void ShowFinished(SignInFinish finish, string message);
    void Bring();
    void Close();
}

internal enum SignInConnectionState { Connected, ChooseAccount, Failed }

internal sealed record SignInConnection(SignInConnectionState State, string? Message = null);

// Drives GitHub CLI browser sign-in with the tray's own window instead of a console. It holds no UI types: the window, the thread hop
// and the account import are supplied by the host, which keeps the flow testable.
internal sealed class GitHubSignInFlow : IDisposable
{
    private readonly GitHubCliSignIn signIn;
    private readonly Action<Action> post;
    private readonly Func<ISignInDialog> createDialog;
    private readonly Func<Task<SignInConnection>> connect;
    private readonly Action showChoices;
    private readonly Action fallback;
    private readonly object gate = new();
    private GitHubCliSignInRun? run;
    private bool active;
    private bool disposed;
    // Touched only from actions passed to post, so it needs no lock.
    private ISignInDialog? dialog;

    public GitHubSignInFlow(GitHubCliSignIn signIn, Action<Action> post, Func<ISignInDialog> createDialog,
        Func<Task<SignInConnection>> connect, Action showChoices, Action fallback)
    {
        this.signIn = signIn;
        this.post = post;
        this.createDialog = createDialog;
        this.connect = connect;
        this.showChoices = showChoices;
        this.fallback = fallback;
    }

    // Returns false when a sign-in is already running (its window is brought forward). Throws Win32Exception or FileNotFoundException
    // when gh cannot be started; no window is shown in that case.
    public bool Begin()
    {
        lock (gate)
        {
            if (disposed) return false;
            if (active)
            {
                post(() => dialog?.Bring());
                return false;
            }
            active = true;
        }
        // Queued before gh starts so a window left over from an earlier attempt is gone before any new code can be shown.
        post(CloseDialog);

        GitHubCliSignInRun started;
        try
        {
            started = signIn.Begin(code => post(() => ShowCode(code.Code)));
        }
        catch
        {
            lock (gate) active = false;
            throw;
        }

        lock (gate)
        {
            run = started;
            if (disposed) started.Cancel();
        }
        post(() => Dialog());
        _ = ObserveAsync(started);
        return true;
    }

    public void Dispose()
    {
        GitHubCliSignInRun? current;
        lock (gate)
        {
            disposed = true;
            current = run;
        }
        current?.Cancel();
        post(CloseDialog);
    }

    private async Task ObserveAsync(GitHubCliSignInRun started)
    {
        try
        {
            var result = await started.Completion.ConfigureAwait(false);
            switch (result.Outcome)
            {
                case GitHubCliSignInOutcome.NoCode:
                    // gh did not print a code we recognise (for example a changed output format): use the visible console as before.
                    Finish(started);
                    post(CloseDialog);
                    fallback();
                    return;
                case GitHubCliSignInOutcome.Cancelled:
                    Finish(started);
                    post(CloseDialog);
                    return;
                case GitHubCliSignInOutcome.TimedOut:
                    Finish(started);
                    post(() => Dialog().ShowFinished(SignInFinish.Retry, "GitHub sign-in timed out because the code was not approved in time. Try again for a new code."));
                    return;
                case GitHubCliSignInOutcome.Failed:
                    Finish(started);
                    post(() => Dialog().ShowFinished(SignInFinish.Retry, "GitHub sign-in did not complete. The code may have expired or access was not approved. Try again for a new code."));
                    return;
                default:
                    post(() => Dialog().ShowConnecting());
                    var connection = await connect().ConfigureAwait(false);
                    Finish(started);
                    post(() => ShowConnection(connection));
                    return;
            }
        }
        catch (OperationCanceledException)
        {
            Finish(started);
        }
        catch (Exception)
        {
            Finish(started);
            post(() => Dialog().ShowFinished(SignInFinish.Retry, "GitHub sign-in could not be completed. Try again."));
        }
    }

    private void Finish(GitHubCliSignInRun started)
    {
        lock (gate)
        {
            if (!ReferenceEquals(run, started)) return;
            run = null;
            active = false;
        }
    }

    private void ShowConnection(SignInConnection connection)
    {
        var view = Dialog();
        switch (connection.State)
        {
            case SignInConnectionState.Connected:
                view.ShowFinished(SignInFinish.Success, "GitHub account connected. Usage will appear shortly.");
                break;
            case SignInConnectionState.ChooseAccount:
                view.ShowFinished(SignInFinish.ChooseAccount, "Several GitHub accounts are signed in to GitHub CLI. Choose which one to use.");
                break;
            default:
                view.ShowFinished(SignInFinish.Retry, string.IsNullOrWhiteSpace(connection.Message)
                    ? "Signed in to GitHub CLI, but the account could not be connected. Try again."
                    : "Signed in to GitHub CLI, but the account could not be connected: " + connection.Message);
                break;
        }
    }

    private void ShowCode(string code)
    {
        var view = Dialog();
        view.ShowCode(code);
        view.Bring();
    }

    private ISignInDialog Dialog()
    {
        if (dialog != null) return dialog;
        var created = createDialog();
        created.CancelRequested += CancelCurrent;
        created.RetryRequested += Retry;
        created.ChoicesRequested += () =>
        {
            CloseDialog();
            showChoices();
        };
        dialog = created;
        created.Bring();
        return created;
    }

    private void CloseDialog()
    {
        var closing = dialog;
        dialog = null;
        closing?.Close();
    }

    private void CancelCurrent()
    {
        GitHubCliSignInRun? current;
        lock (gate) current = run;
        current?.Cancel();
    }

    private void Retry()
    {
        try
        {
            Begin();
        }
        catch (Exception)
        {
            // Begin has already queued the close of the previous window; posting keeps this message after it.
            post(() => Dialog().ShowFinished(SignInFinish.Retry, "GitHub CLI could not be started. Check that it is installed, then try again."));
        }
    }
}
