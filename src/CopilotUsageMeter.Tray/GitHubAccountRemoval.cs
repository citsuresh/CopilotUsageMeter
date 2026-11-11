using System;
using System.Threading;
using System.Threading.Tasks;
using CopilotUsageMeter.Readers.GitHub;

namespace CopilotUsageMeter.Tray;

internal sealed record AccountRemoval(bool Succeeded, string Message);

// Removes a saved GitHub CLI login and, when it is the account the tray is connected with, also clears the tray's own copy.
internal sealed class GitHubAccountRemoval
{
    private readonly Func<string, CancellationToken, Task<GitHubCliLogoutOutcome>> logout;
    private readonly Func<string?> connectedLogin;
    private readonly Func<Task<bool>> signOutTray;
    private readonly Action published;
    private readonly SemaphoreSlim gate = new(1, 1);

    public GitHubAccountRemoval(Func<string, CancellationToken, Task<GitHubCliLogoutOutcome>> logout, Func<string?> connectedLogin, Func<Task<bool>> signOutTray, Action published)
    {
        this.logout = logout;
        this.connectedLogin = connectedLogin;
        this.signOutTray = signOutTray;
        this.published = published;
    }

    public async Task<AccountRemoval> RemoveAsync(string login, CancellationToken cancellationToken)
    {
        // One removal at a time: gh rewrites its own configuration.
        if (!await gate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
            return new AccountRemoval(false, "Another account is still being removed. Try again in a moment.");
        try
        {
            var outcome = await logout(login, cancellationToken).ConfigureAwait(false);
            switch (outcome)
            {
                case GitHubCliLogoutOutcome.Removed:
                    var wasConnected = string.Equals(connectedLogin(), login, StringComparison.OrdinalIgnoreCase);
                    var signedOut = !wasConnected || await signOutTray().ConfigureAwait(false);
                    published();
                    if (!signedOut) return new AccountRemoval(true, "Removed " + login + " from GitHub CLI, but the tray's own copy could not be cleared. Use Log out.");
                    return new AccountRemoval(true, "Removed " + login + " from GitHub CLI." + (wasConnected ? " The tray was signed out because it used that account." : ""));
                case GitHubCliLogoutOutcome.Unavailable:
                    return new AccountRemoval(false, "GitHub CLI (gh) is not installed or not on PATH, so the account was not removed.");
                case GitHubCliLogoutOutcome.TimedOut:
                    return new AccountRemoval(false, "GitHub CLI did not finish removing the account. Try again, or run gh auth logout yourself.");
                case GitHubCliLogoutOutcome.InvalidLogin:
                    return new AccountRemoval(false, "That is not a valid GitHub login.");
                default:
                    return new AccountRemoval(false, "GitHub CLI could not remove the account. Run gh auth status to check, or remove it with gh auth logout.");
            }
        }
        finally { gate.Release(); }
    }
}
