using My.Shared.Rules;
using Xunit;

namespace My.Tests.Rules;

public class GoogleDriveOAuthRulesTests
{
    [Fact]
    public void Connect_scopes_include_drive_file_and_readonly()
    {
        Assert.Contains(GoogleDriveOAuthRules.DriveFileScope, GoogleDriveOAuthRules.ConnectScopes);
        Assert.Contains(GoogleDriveOAuthRules.DriveReadonlyScope, GoogleDriveOAuthRules.ConnectScopes);
    }

    [Fact]
    public void Connect_scopes_do_not_request_calendar() =>
        Assert.DoesNotContain(GoogleCalendarOAuthRules.CalendarScope, GoogleDriveOAuthRules.ConnectScopes);

    [Theory]
    [InlineData(false, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    public void NeedsDriveConsent_when_token_or_grant_missing(bool hasToken, bool granted, bool needed) =>
        Assert.Equal(needed, GoogleDriveOAuthRules.NeedsDriveConsent(hasToken, granted));

    [Fact]
    public void IsConsentRequiredMessage_matches_server_text() =>
        Assert.True(GoogleDriveOAuthRules.IsConsentRequiredMessage(GoogleDriveOAuthRules.ConsentRequiredMessage));
}