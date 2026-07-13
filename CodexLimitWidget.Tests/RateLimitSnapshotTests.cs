using System.Text.Json;
using CodexLimitWidget.Core;
using Xunit;

namespace CodexLimitWidget.Tests;

public sealed class RateLimitSnapshotTests
{
    [Fact]
    public void ParsesCodexBucketAndCalculatesRemainingPercent()
    {
        using var json = JsonDocument.Parse("""{ "rateLimitsByLimitId": { "codex": { "limitId": "codex", "planType": "pro", "primary": { "usedPercent": 25, "windowDurationMins": 300 }, "secondary": { "usedPercent": 60 } } } }""");
        var snapshot = RateLimitSnapshot.FromJson(json.RootElement);
        Assert.Equal("pro", snapshot.PlanType);
        Assert.Equal(75, snapshot.RemainingPercent);
        Assert.Equal(60, snapshot.Secondary.UsedPercent);
        Assert.True(snapshot.HasFiveHourLimit);
    }

    [Fact]
    public void DetectsTemporaryResponseWithoutFiveHourWindow()
    {
        using var json = JsonDocument.Parse("""{ "rateLimitsByLimitId": { "codex": { "limitId": "codex", "planType": "pro", "primary": { "usedPercent": 60, "windowDurationMins": 10080 } } } }""");
        var snapshot = RateLimitSnapshot.FromJson(json.RootElement);
        Assert.Equal(60, snapshot.Primary.UsedPercent);
        Assert.False(snapshot.HasFiveHourLimit);
    }

    [Fact]
    public void RejectsResponseWithoutCodexBucket()
    {
        using var json = JsonDocument.Parse("{}");
        Assert.Throws<InvalidOperationException>(() => RateLimitSnapshot.FromJson(json.RootElement));
    }

    [Fact]
    public void RejectsNonObjectCodexBucket()
    {
        using var json = JsonDocument.Parse("""{ "rateLimitsByLimitId": { "codex": [] } }""");
        Assert.Throws<InvalidOperationException>(() => RateLimitSnapshot.FromJson(json.RootElement));
    }

    [Fact]
    public void IgnoresInvalidOptionalWindowValues()
    {
        using var json = JsonDocument.Parse("""{ "rateLimits": { "primary": { "usedPercent": "bad", "resetsAt": null } } }""");
        var snapshot = RateLimitSnapshot.FromJson(json.RootElement);
        Assert.Null(snapshot.Primary.UsedPercent);
        Assert.Null(snapshot.Primary.ResetsAt);
    }
}
