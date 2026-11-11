using CopilotUsageMeter.Contracts;
using CopilotUsageMeter.Core;
using CopilotUsageMeter.Tray;

namespace CopilotUsageMeter.Architecture.Tests;

public sealed class HealthAlertTests
{
    private static readonly DateTimeOffset Start = new(2031, 5, 10, 12, 0, 0, TimeSpan.Zero);

    private static AccountReadResult Result(string status, string? code = null) => AccountReadResult.Empty(status, code);

    private sealed class Harness
    {
        public DateTimeOffset Now = Start;
        public bool Enabled = true;
        public readonly List<HealthCondition> Shown = new();
        public readonly HealthAlertService Service;
        private int sequence;

        public Harness() => Service = new HealthAlertService(() => Enabled, alert => Shown.Add(alert.Condition), () => Now);

        // Each call stands for one completed read.
        public void Read(AccountReadResult result, bool launch = false) => Service.OnAccount(result, ++sequence, launch);

        // The same result published again for an unrelated reason (schedule or progress change).
        public void Republish(AccountReadResult result) => Service.OnAccount(result, sequence, launch: false);
    }

    [Theory]
    [InlineData("signed-out", null)]
    [InlineData("signed-out", "cli-login-not-found")]
    [InlineData("signed-out", "secure-credential-required")]
    public void LaunchWithoutSignIn_NotifiesOnce(string status, string? code)
    {
        var h = new Harness();

        h.Read(Result(status, code), launch: true);
        h.Read(Result(status, code));
        h.Republish(Result(status, code));

        Assert.Equal(new[] { HealthCondition.SignInNeeded }, h.Shown);
    }

    [Fact]
    public void SigningOutAfterLaunch_DoesNotNotify()
    {
        var h = new Harness();
        h.Read(Result("available"), launch: true);

        h.Read(Result("signed-out"));

        Assert.Empty(h.Shown);
    }

    [Fact]
    public void SkippedAccountAtLaunch_DoesNotNotify()
    {
        var h = new Harness();

        h.Read(Result("skipped"), launch: true);

        Assert.Empty(h.Shown);
    }

    [Fact]
    public void ExpiredAuthorization_NotifiesAndIsNotConfusedWithSignOut()
    {
        var h = new Harness();

        h.Read(Result("signed-out", "authorization-expired"), launch: true);

        Assert.Equal(new[] { HealthCondition.AuthorizationExpired }, h.Shown);
    }

    [Fact]
    public void ExpiredAuthorization_NotifiesEvenWhenNoNewReadIsCounted()
    {
        // The coordinator republishes the 401 result through an authorization change, without advancing its read sequence.
        var h = new Harness();
        h.Read(Result("available"), launch: true);
        var expired = Result("signed-out", "authorization-expired");

        h.Republish(expired);
        h.Republish(expired);

        Assert.Equal(new[] { HealthCondition.AuthorizationExpired }, h.Shown);
    }

    [Fact]
    public void RepublishedResult_IsNotCountedAsAnotherFailure()
    {
        var h = new Harness();
        var failed = Result("error", "github-http-failed");

        h.Read(failed);
        for (var i = 0; i < 10; i++) h.Republish(failed);

        Assert.Empty(h.Shown);
    }

    [Fact]
    public void RepeatedGitHubErrors_NotifyOnThirdReadOnly()
    {
        var h = new Harness();
        var failed = Result("error", "github-http-failed");

        h.Read(failed);
        h.Read(failed);
        Assert.Empty(h.Shown);
        h.Read(failed);
        h.Read(failed);

        Assert.Equal(new[] { HealthCondition.GitHubError }, h.Shown);
    }

    [Fact]
    public void ShortNetworkOutage_DoesNotNotify_LongOneDoesOnce()
    {
        var h = new Harness();
        var offline = Result("error", "github-request-failed");

        h.Read(offline);
        h.Now += TimeSpan.FromMinutes(5);
        h.Read(offline);
        h.Now += TimeSpan.FromMinutes(10);
        h.Read(offline);
        Assert.Empty(h.Shown);

        h.Now += TimeSpan.FromMinutes(20);
        h.Read(Result("error", "github-request-timeout"));
        h.Now += TimeSpan.FromMinutes(15);
        h.Read(offline);

        Assert.Equal(new[] { HealthCondition.GitHubUnreachable }, h.Shown);
    }

    [Fact]
    public void RecoveryRearms_ButCooldownStopsAFlappingProblem()
    {
        var h = new Harness();
        var failed = Result("error", "github-http-failed");
        for (var i = 0; i < 3; i++) h.Read(failed);
        h.Read(Result("available"));

        h.Now += TimeSpan.FromHours(1);
        for (var i = 0; i < 3; i++) h.Read(failed);
        Assert.Single(h.Shown);

        h.Read(Result("available"));
        h.Now += HealthAlertTracker.Cooldown;
        for (var i = 0; i < 3; i++) h.Read(failed);

        Assert.Equal(2, h.Shown.Count);
    }

    [Fact]
    public void UsageUnavailableForAccount_NotifiesAfterTwoReads()
    {
        var h = new Harness();

        h.Read(Result("unavailable", "quota-unavailable"));
        Assert.Empty(h.Shown);
        h.Read(Result("unavailable", "quota-unavailable"));

        Assert.Equal(new[] { HealthCondition.AccountUsageUnavailable }, h.Shown);
    }

    [Fact]
    public void RateLimiting_IsIgnoredAndDoesNotResetProgress()
    {
        var h = new Harness();
        var failed = Result("error", "github-http-failed");

        h.Read(failed);
        h.Read(Result("rate-limited", "github-rate-limited"));
        h.Read(failed);
        h.Read(failed);

        Assert.Equal(new[] { HealthCondition.GitHubError }, h.Shown);
    }

    [Fact]
    public void ChangingFailureKind_RestartsTheCount()
    {
        var h = new Harness();

        h.Read(Result("error", "github-http-failed"));
        h.Read(Result("error", "github-http-failed"));
        h.Read(Result("unavailable", "quota-unavailable"));
        h.Read(Result("error", "github-http-failed"));

        Assert.Empty(h.Shown);
    }

    [Fact]
    public void Disabled_RemembersTheProblemSoEnablingLaterIsQuiet()
    {
        var h = new Harness { Enabled = false };
        var failed = Result("error", "github-http-failed");
        for (var i = 0; i < 4; i++) h.Read(failed);

        h.Enabled = true;
        h.Read(failed);

        Assert.Empty(h.Shown);
    }

    [Fact]
    public void LocalReadFailure_NotifiesOnSecondFailureAndClearsOnSuccess()
    {
        var h = new Harness();

        h.Service.OnLocal("error", "history_process_failed");
        Assert.Empty(h.Shown);
        h.Service.OnLocal("error", "history_process_failed");
        h.Service.OnLocal("error", "history_process_failed");
        h.Service.OnLocal("available", null);

        Assert.Equal(new[] { HealthCondition.LocalReadFailing }, h.Shown);
    }

    [Theory]
    [InlineData("unavailable", "history_not_recorded")]
    [InlineData("unavailable", "history_no_turns")]
    [InlineData("available", null)]
    public void NormalLocalStates_DoNotNotify(string status, string? code)
    {
        var h = new Harness();

        for (var i = 0; i < 5; i++) h.Service.OnLocal(status, code);

        Assert.Empty(h.Shown);
    }

    [Fact]
    public void MissingHistoryTool_NotifiesImmediatelyAsSetupProblem()
    {
        var h = new Harness();

        h.Service.OnLocal("error", "history_tool_missing");

        Assert.Equal(new[] { HealthCondition.LocalSetupProblem }, h.Shown);
    }

    [Fact]
    public void BackgroundFault_NotifiesOnceWithinCooldown()
    {
        var h = new Harness();

        h.Service.OnFault();
        h.Now += TimeSpan.FromMinutes(10);
        h.Service.OnFault();
        h.Now += HealthAlertTracker.Cooldown;
        h.Service.OnFault();

        Assert.Equal(new[] { HealthCondition.TrackingFault, HealthCondition.TrackingFault }, h.Shown);
    }

    [Fact]
    public void FailingPresenter_DoesNotThrow()
    {
        var service = new HealthAlertService(() => true, _ => throw new InvalidOperationException("Sensitive title"), () => Start);

        service.OnFault();
    }

    [Fact]
    public void AlertText_IsFixedAndFitsBalloonLimits()
    {
        foreach (var condition in Enum.GetValues<HealthCondition>())
        {
            var title = HealthAlertText.Title(condition);
            var body = HealthAlertText.Body(condition);

            Assert.InRange(title.Length, 1, 63);
            Assert.InRange(body.Length, 1, 255);
            Assert.DoesNotContain("{", title + body);
        }
    }

    [Fact]
    public void Settings_DefaultToNotifyingAndKeepTheChoice()
    {
        Assert.True(new AlertSettings().NotifyProblems);
        Assert.False(new AlertSettings { NotifyProblems = false }.Normalized().NotifyProblems);
    }

    [Fact]
    public void SettingsFileWithoutTheProblemSetting_DefaultsToNotifying()
    {
        var directory = Path.Combine(Path.GetTempPath(), "cum-health-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "alerts.json");
            File.WriteAllText(path, "{\"Enabled\":true,\"Levels\":[{\"Percent\":50,\"Channel\":1}]}");
            var store = new AlertSettingsStore(path);

            var loaded = store.Load();
            store.Save(new AlertSettings { NotifyProblems = false });

            Assert.True(loaded.NotifyProblems);
            Assert.False(store.Load().NotifyProblems);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
