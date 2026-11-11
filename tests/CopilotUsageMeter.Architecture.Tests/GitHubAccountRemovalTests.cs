using CopilotUsageMeter.Readers.GitHub;
using CopilotUsageMeter.Tray;

namespace CopilotUsageMeter.Architecture.Tests;

public sealed class GitHubAccountRemovalTests
{
    private sealed class Harness
    {
        public GitHubCliLogoutOutcome Outcome = GitHubCliLogoutOutcome.Removed;
        public string? Connected;
        public bool SignOutSucceeds = true;
        public List<string> LoggedOut = new();
        public int SignOuts;
        public int Published;
        public Task? Hold;

        public GitHubAccountRemoval Create() => new(
            async (login, _) =>
            {
                LoggedOut.Add(login);
                if (Hold != null) await Hold;
                return Outcome;
            },
            () => Connected,
            () => { SignOuts++; return Task.FromResult(SignOutSucceeds); },
            () => Published++);
    }

    [Fact]
    public async Task Removing_another_account_leaves_the_tray_connected()
    {
        var h = new Harness { Connected = "user-b" };
        var result = await h.Create().RemoveAsync("user-a", CancellationToken.None);
        Assert.True(result.Succeeded);
        Assert.Equal(new[] { "user-a" }, h.LoggedOut);
        Assert.Equal(0, h.SignOuts);
        Assert.Equal(1, h.Published);
        Assert.DoesNotContain("signed out", result.Message);
    }

    [Fact]
    public async Task Removing_the_connected_account_also_signs_the_tray_out_ignoring_case()
    {
        var h = new Harness { Connected = "User-A" };
        var result = await h.Create().RemoveAsync("user-a", CancellationToken.None);
        Assert.True(result.Succeeded);
        Assert.Equal(1, h.SignOuts);
        Assert.Equal(1, h.Published);
        Assert.Contains("tray was signed out", result.Message);
    }

    [Fact]
    public async Task A_failed_tray_sign_out_is_reported_but_the_removal_still_counts()
    {
        var h = new Harness { Connected = "user-a", SignOutSucceeds = false };
        var result = await h.Create().RemoveAsync("user-a", CancellationToken.None);
        Assert.True(result.Succeeded);
        Assert.Contains("Use Log out", result.Message);
        Assert.Equal(1, h.Published);
    }

    [Theory]
    [InlineData(GitHubCliLogoutOutcome.Failed)]
    [InlineData(GitHubCliLogoutOutcome.Unavailable)]
    [InlineData(GitHubCliLogoutOutcome.TimedOut)]
    [InlineData(GitHubCliLogoutOutcome.InvalidLogin)]
    public async Task A_failed_removal_changes_nothing_in_the_tray(GitHubCliLogoutOutcome outcome)
    {
        var h = new Harness { Outcome = outcome, Connected = "user-a" };
        var result = await h.Create().RemoveAsync("user-a", CancellationToken.None);
        Assert.False(result.Succeeded);
        Assert.Equal(0, h.SignOuts);
        Assert.Equal(0, h.Published);
        Assert.False(string.IsNullOrWhiteSpace(result.Message));
    }

    [Fact]
    public async Task A_second_removal_while_one_is_running_is_refused_without_calling_gh()
    {
        var release = new TaskCompletionSource();
        var h = new Harness { Hold = release.Task };
        var removal = h.Create();
        var first = removal.RemoveAsync("user-a", CancellationToken.None);
        var second = await removal.RemoveAsync("user-b", CancellationToken.None);
        Assert.False(second.Succeeded);
        Assert.Equal(new[] { "user-a" }, h.LoggedOut);
        release.SetResult();
        Assert.True((await first).Succeeded);
        var third = await removal.RemoveAsync("user-b", CancellationToken.None);
        Assert.True(third.Succeeded);
    }

    [Fact]
    public async Task The_removal_slot_is_released_when_gh_throws()
    {
        var calls = 0;
        var removal = new GitHubAccountRemoval(
            (_, _) => ++calls == 1 ? throw new OperationCanceledException() : Task.FromResult(GitHubCliLogoutOutcome.Removed),
            () => null, () => Task.FromResult(true), () => { });
        await Assert.ThrowsAsync<OperationCanceledException>(() => removal.RemoveAsync("user-a", CancellationToken.None));
        Assert.True((await removal.RemoveAsync("user-a", CancellationToken.None)).Succeeded);
    }
}

public sealed class GitHubCliLogoutTests
{
    private static GitHubCliLogout Cmd(string format, TimeSpan? timeout = null) => new("cmd.exe", format, timeout);

    [Fact]
    public async Task Exit_code_zero_is_removed()
        => Assert.Equal(GitHubCliLogoutOutcome.Removed, await Cmd("/c echo {0}").RemoveAsync("user-a", CancellationToken.None));

    [Fact]
    public async Task A_non_zero_exit_code_is_failed()
        => Assert.Equal(GitHubCliLogoutOutcome.Failed, await Cmd("/c echo {0}& exit 3").RemoveAsync("user-a", CancellationToken.None));

    [Fact]
    public async Task A_missing_program_is_unavailable()
        => Assert.Equal(GitHubCliLogoutOutcome.Unavailable,
            await new GitHubCliLogout("definitely-not-a-real-gh-program.exe").RemoveAsync("user-a", CancellationToken.None));

    [Theory]
    [InlineData("")]
    [InlineData("a b")]
    [InlineData("a&calc")]
    [InlineData("-x")]
    [InlineData("user-a --hostname evil.example")]
    public async Task An_invalid_login_is_rejected_before_any_process_starts(string login)
        => Assert.Equal(GitHubCliLogoutOutcome.InvalidLogin,
            await new GitHubCliLogout("definitely-not-a-real-gh-program.exe").RemoveAsync(login, CancellationToken.None));

    [Fact]
    public async Task A_process_that_never_finishes_times_out()
        => Assert.Equal(GitHubCliLogoutOutcome.TimedOut,
            await Cmd("/c ping -n 20 127.0.0.1 >nul & echo {0}", TimeSpan.FromMilliseconds(400)).RemoveAsync("user-a", CancellationToken.None));

    [Fact]
    public async Task Cancellation_stops_the_process_and_is_reported_as_cancellation()
    {
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Cmd("/c ping -n 20 127.0.0.1 >nul & echo {0}").RemoveAsync("user-a", cancel.Token));
    }

    [Fact]
    public void The_default_command_removes_one_named_user_on_github_com_only()
        => Assert.Equal("auth logout --hostname github.com --user user-a", string.Format(GitHubCliLogout.DefaultArgumentFormat, "user-a"));
}
