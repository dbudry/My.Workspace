using My.Shared.Rules;
using Xunit;

namespace My.Tests.Rules;

public class GoogleCalendarOAuthRulesTests
{
    [Fact]
    public void Connect_scopes_are_calendar_email_and_openid_only()
    {
        Assert.Equal(
            new[]
            {
                GoogleCalendarOAuthRules.CalendarScope,
                GoogleCalendarOAuthRules.EmailScope,
                GoogleCalendarOAuthRules.OpenIdScope
            },
            GoogleCalendarOAuthRules.ConnectScopes);
    }

    [Fact]
    public void Connect_scopes_do_not_request_drive() =>
        Assert.DoesNotContain(GoogleCalendarOAuthRules.ConnectScopes, GoogleCalendarOAuthRules.IsDriveScope);

    [Theory]
    [InlineData("https://www.googleapis.com/auth/drive")]
    [InlineData("https://www.googleapis.com/auth/drive.file")]
    [InlineData("https://www.googleapis.com/auth/drive.readonly")]
    public void IsDriveScope_detects_drive_family(string scope) =>
        Assert.True(GoogleCalendarOAuthRules.IsDriveScope(scope));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("openid")]
    [InlineData("https://www.googleapis.com/auth/calendar")]
    [InlineData("https://www.googleapis.com/auth/userinfo.email")]
    public void IsDriveScope_rejects_non_drive(string? scope) =>
        Assert.False(GoogleCalendarOAuthRules.IsDriveScope(scope));

    [Theory]
    [InlineData(null, false, false, false)]
    [InlineData("", true, false, false)]
    [InlineData("new-token", false, false, true)]
    [InlineData("new-token", true, false, true)]
    [InlineData("new-token", true, true, false)]
    [InlineData("new-token", false, true, true)]
    public void ShouldOverwriteCalendarRefreshToken_keeps_drive_grant(
        string? incoming, bool hasExisting, bool driveGranted, bool expected) =>
        Assert.Equal(
            expected,
            GoogleCalendarOAuthRules.ShouldOverwriteCalendarRefreshToken(incoming, hasExisting, driveGranted));

    [Theory]
    [InlineData(false, false, "new-token", false)]
    [InlineData(false, true, "new-token", false)]
    [InlineData(true, false, "new-token", false)]
    [InlineData(true, true, null, false)]
    [InlineData(true, true, "", false)]
    [InlineData(true, true, "new-token", true)]
    public void ShouldRetryWatchWithFreshToken_requires_failure_kept_token_and_a_replacement(
        bool watchStartFailed, bool keptExistingToken, string? incomingRefreshToken, bool expected) =>
        Assert.Equal(
            expected,
            GoogleCalendarOAuthRules.ShouldRetryWatchWithFreshToken(
                watchStartFailed, keptExistingToken, incomingRefreshToken));

    [Theory]
    [InlineData("invalid_grant", true)]
    [InlineData("INVALID_GRANT", true)]
    [InlineData("invalid_client", false)]
    [InlineData("temporarily_unavailable", false)]
    [InlineData(null, false)]
    [InlineData("", false)]
    public void IsRevokedTokenError_only_matches_invalid_grant(string? errorCode, bool expected) =>
        Assert.Equal(expected, GoogleCalendarOAuthRules.IsRevokedTokenError(errorCode));

    [Fact]
    public void AppendHostedDomainHint_adds_hd_on_query_url()
    {
        var url = GoogleCalendarOAuthRules.AppendHostedDomainHint(
            "https://accounts.google.com/o/oauth2/v2/auth?client_id=x",
            "example.com");
        Assert.Equal(
            "https://accounts.google.com/o/oauth2/v2/auth?client_id=x&hd=example.com",
            url);
        Assert.DoesNotContain("login_hint", url, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AppendHostedDomainHint_adds_hd_when_url_has_no_query()
    {
        var url = GoogleCalendarOAuthRules.AppendHostedDomainHint(
            "https://accounts.google.com/o/oauth2/v2/auth",
            "example.com");
        Assert.Equal(
            "https://accounts.google.com/o/oauth2/v2/auth?hd=example.com",
            url);
    }

    [Fact]
    public void AppendHostedDomainHint_skips_when_domain_null_or_blank()
    {
        const string baseUrl = "https://accounts.google.com/o/oauth2/v2/auth?client_id=x";
        Assert.Equal(baseUrl, GoogleCalendarOAuthRules.AppendHostedDomainHint(baseUrl, null));
        Assert.Equal(baseUrl, GoogleCalendarOAuthRules.AppendHostedDomainHint(baseUrl, "  "));
    }
}
