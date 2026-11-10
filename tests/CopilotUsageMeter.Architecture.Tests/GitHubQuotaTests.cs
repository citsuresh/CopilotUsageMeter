using CopilotUsageMeter.Core;
using CopilotUsageMeter.Readers.GitHub;

namespace CopilotUsageMeter.Architecture.Tests;

public sealed class GitHubQuotaTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-01-01T00:00:00Z");

    [Fact]
    public void PreservesReportedNumbersRatherThanDerivingRemaining()
    {
        var result = GitHubQuotaParser.Parse("""
            {"copilot_plan":"business","token_based_billing":false,"quota_reset_date":"2026-02-01",
             "quota_snapshots":{"premium_interactions":{"entitlement":100,"credits_used":12.5,"remaining":80,"percent_remaining":88,"has_quota":true,"unlimited":false}}}
            """, Now);
        Assert.Equal("available", result.Status);
        Assert.Equal(100m, result.Usage.Allowance);
        Assert.Equal(12.5m, result.Usage.Consumed);
        Assert.Equal(80m, result.Usage.Remaining);
        Assert.Equal(Now, result.Usage.AsOfUtc);
        Assert.Equal("business", result.Plan);
        Assert.False(result.TokenBasedBilling);
        Assert.Equal(Now.AddMonths(1), result.ResetUtc);
        Assert.Contains("unsupported", result.Source);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"quota_snapshots\":null}")]
    [InlineData("{\"quota_snapshots\":{\"chat\":{\"unlimited\":true}}}")]
    [InlineData("{\"quota_snapshots\":{\"premium_interactions\":{\"remaining\":null,\"entitlement\":null,\"credits_used\":null}}}")]
    [InlineData("{\"quota_snapshots\":{\"premium_interactions\":{\"has_quota\":false,\"remaining\":10}}}")]
    [InlineData("{\"quota_snapshots\":{\"premium_interactions\":{\"remaining\":-1,\"entitlement\":\"100\",\"credits_used\":true}}}")]
    public void MissingUnknownOrInvalidQuotaIsNotFabricatedZero(string json)
    {
        var result = GitHubQuotaParser.Parse(json, Now);
        Assert.Equal("unavailable", result.Status);
        Assert.Null(result.Usage.Allowance);
        Assert.Null(result.Usage.Consumed);
        Assert.Null(result.Usage.Remaining);
        Assert.Null(result.Usage.AsOfUtc);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("\"enabled\"")]
    [InlineData("[true]")]
    public void StructuredBillingShapesAreExplicitlyUnsupportedEvenWhenLegacyQuotaExists(string billing)
    {
        var result = GitHubQuotaParser.Parse("{\"token_based_billing\":" + billing + ",\"quota_snapshots\":{\"premium_interactions\":{\"remaining\":10}}}", Now);
        Assert.Equal("unavailable", result.Status);
        Assert.Equal("unsupported-billing", result.ErrorCode);
        Assert.Null(result.Usage.Remaining);
    }

    [Fact]
    public void TokenBilledSnapshotWithFiniteCreditsIsAvailable()
    {
        var result = GitHubQuotaParser.Parse("""
            {"copilot_plan":"business","token_based_billing":true,"quota_reset_date_utc":"2026-02-01T00:00:00.000Z",
             "quota_snapshots":{"chat":{"unlimited":true,"entitlement":0,"credits_used":0,"remaining":0},
             "premium_interactions":{"entitlement":100000,"credits_used":6339,"remaining":93548,"quota_remaining":93548.9,"percent_remaining":93.5,"has_quota":true,"unlimited":false,"token_based_billing":true}}}
            """, Now);
        Assert.Equal("available", result.Status);
        Assert.Null(result.ErrorCode);
        Assert.Equal(100000m, result.Usage.Allowance);
        Assert.Equal(6339m, result.Usage.Consumed);
        Assert.Equal(93548m, result.Usage.Remaining);
        Assert.True(result.TokenBasedBilling);
    }

    [Theory]
    [InlineData("{\"remaining\":10}")]
    [InlineData("{\"entitlement\":100000,\"remaining\":10}")]
    [InlineData("{\"credits_used\":5,\"remaining\":10}")]
    public void TokenBilledSnapshotWithoutFiniteAllowanceAndConsumptionIsUnsupported(string premium)
    {
        var result = GitHubQuotaParser.Parse("{\"token_based_billing\":true,\"quota_snapshots\":{\"premium_interactions\":" + premium + "}}", Now);
        Assert.Equal("unavailable", result.Status);
        Assert.Equal("unsupported-billing", result.ErrorCode);
        Assert.Null(result.Usage.Allowance);
        Assert.Null(result.Usage.Consumed);
        Assert.Null(result.Usage.Remaining);
    }

    [Fact]
    public void PlansWithoutPremiumQuotaAreExplicitlyUnsupported()
    {
        var result = GitHubQuotaParser.Parse("{\"copilot_plan\":\"unknown-plan\",\"quota_snapshots\":{}}", Now);
        Assert.Equal("unavailable", result.Status);
        Assert.Equal("unsupported-plan", result.ErrorCode);
        Assert.Null(result.Usage.Remaining);
    }

    [Fact]
    public void UnlimitedDoesNotInventFiniteAllowanceOrConsumption()
    {
        var result = GitHubQuotaParser.Parse("{\"quota_snapshots\":{\"premium_interactions\":{\"unlimited\":true,\"entitlement\":null,\"credits_used\":0,\"remaining\":null}}}", Now);
        Assert.Equal("available", result.Status);
        Assert.True(result.IsUnlimited);
        Assert.Null(result.Usage.Allowance);
        Assert.Equal(0m, result.Usage.Consumed);
        Assert.Null(result.Usage.Remaining);
    }

    [Fact]
    public void ZeroIsDifferentFromMissingAndResetUtcTakesPrecedence()
    {
        var result = GitHubQuotaParser.Parse("""
            {"quota_reset_date":"2026-02-01","quota_reset_date_utc":"2026-02-02T03:00:00+03:00",
             "quota_snapshots":{"premium_interactions":{"remaining":0,"credits_used":null}}}
            """, Now);
        Assert.Equal("available", result.Status);
        Assert.Equal(0m, result.Usage.Remaining);
        Assert.Null(result.Usage.Consumed);
        Assert.Equal(Now.AddMonths(1).AddDays(1), result.ResetUtc);
    }

    [Theory]
    [InlineData(null)]
    public void NullInputIsAProgrammingFaultRatherThanAJsonResponse(string? payload)
    {
        Assert.Throws<ArgumentNullException>(() => GitHubQuotaParser.Parse(payload!, Now));
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("[]")]
    [InlineData("{\"quota_snapshots\":1}")]
    [InlineData("{\"quota_snapshots\":{},\"quota_snapshots\":{}}")]
    [InlineData("{} {}")]
    public void InvalidPayloadIsSanitized(string payload)
    {
        var result = GitHubQuotaParser.Parse(payload, Now);
        Assert.NotEqual("available", result.Status);
        Assert.Null(result.Usage.Remaining);
        Assert.DoesNotContain(payload, result.ErrorMessage ?? "");
    }
}