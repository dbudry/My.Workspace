using My.Shared.Constants;
using My.Shared.Rules;
using Xunit;

namespace My.Tests.Rules;

public class RateLimitRulesTests
{
    private static readonly RateLimitOptions EnabledOptions = RateLimitRules.BuildOptions(true);

    [Theory]
    [InlineData("/api/trackedtasks", "trackedtasks")]
    [InlineData("/api", "")]
    [InlineData("/api/", "")]
    [InlineData("trackedtasks", "trackedtasks")]
    public void NormalizeApiPath_strips_api_prefix(string input, string expected) =>
        Assert.Equal(expected, RateLimitRules.NormalizeApiPath(input));

    [Theory]
    [InlineData("googlecalendar/webhook", "POST", true)]
    [InlineData("trackedtasks", "OPTIONS", true)]
    [InlineData("trackedtasks", "GET", false)]
    public void IsExempt_skips_webhook_and_options(string path, string method, bool expected) =>
        Assert.Equal(expected, RateLimitRules.IsExempt(path, method));

    [Fact]
    public void Resolve_returns_exempt_when_disabled()
    {
        var disabled = RateLimitRules.BuildOptions(false);
        var context = new RateLimitRequestContext("trackedtasks", "GET", "user-1", "1.2.3.4", true);

        var policy = RateLimitRules.Resolve(context, disabled);

        Assert.True(policy.IsExempt);
    }

    [Fact]
    public void Resolve_authenticated_user_gets_default_bucket()
    {
        var context = new RateLimitRequestContext("trackedtasks", "GET", "user-1", "1.2.3.4", true);
        var policy = RateLimitRules.Resolve(context, EnabledOptions);

        Assert.Equal("user:user-1", policy.BucketKey);
        Assert.Equal(RateLimitRules.DefaultAuthenticatedPerMinute, policy.PermitsPerMinute);
    }

    [Fact]
    public void Resolve_invalid_bearer_uses_ip_bucket()
    {
        var context = new RateLimitRequestContext("trackedtasks", "GET", null, "9.9.9.9", true);
        var policy = RateLimitRules.Resolve(context, EnabledOptions);

        Assert.Equal("auth-invalid:9.9.9.9", policy.BucketKey);
        Assert.Equal(RateLimitRules.InvalidBearerPerMinute, policy.PermitsPerMinute);
    }

    [Fact]
    public void Resolve_provision_uses_strict_ip_bucket_even_with_bearer()
    {
        var context = new RateLimitRequestContext(
            Constants.API.User.Provision,
            "POST",
            null,
            "9.9.9.9",
            true);

        var policy = RateLimitRules.Resolve(context, EnabledOptions);

        Assert.Equal("provision:9.9.9.9", policy.BucketKey);
        Assert.Equal(RateLimitRules.ProvisionPerMinute, policy.PermitsPerMinute);
    }

    [Fact]
    public void Resolve_upload_uses_strict_user_bucket()
    {
        var context = new RateLimitRequestContext(
            "intranet/pages/abc/documents/upload",
            "POST",
            "user-1",
            "1.2.3.4",
            true);

        var policy = RateLimitRules.Resolve(context, EnabledOptions);

        Assert.Equal("user:user-1:upload", policy.BucketKey);
        Assert.Equal(RateLimitRules.UploadPerMinute, policy.PermitsPerMinute);
    }

    [Fact]
    public void Resolve_logs_uses_heavy_read_bucket()
    {
        var context = new RateLimitRequestContext(
            Constants.API.Logs.Get,
            "GET",
            "admin-1",
            "1.2.3.4",
            true);

        var policy = RateLimitRules.Resolve(context, EnabledOptions);

        Assert.Equal("user:admin-1:heavy-read", policy.BucketKey);
        Assert.Equal(RateLimitRules.HeavyReadPerMinute, policy.PermitsPerMinute);
    }

    [Theory]
    [InlineData(0, 60)]
    [InlineData(30, 30)]
    [InlineData(59, 1)]
    public void ComputeRetryAfterSeconds_counts_down_within_minute(int secondsIntoMinute, int expected)
    {
        var utcNow = DateTimeOffset.FromUnixTimeSeconds(secondsIntoMinute);
        Assert.Equal(expected, RateLimitRules.ComputeRetryAfterSeconds(utcNow));
    }
}