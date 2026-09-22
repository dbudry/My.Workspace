using My.Shared.Rules;
using Xunit;

namespace My.Tests.Rules;

public class GoogleAppDriveOAuthRulesTests
{
    [Fact]
    public void Connect_scopes_use_full_drive_not_file_or_readonly()
    {
        Assert.Contains(GoogleAppDriveOAuthRules.DriveScope, GoogleAppDriveOAuthRules.ConnectScopes);
        Assert.DoesNotContain(GoogleDriveOAuthRules.DriveFileScope, GoogleAppDriveOAuthRules.ConnectScopes);
        Assert.DoesNotContain(GoogleDriveOAuthRules.DriveReadonlyScope, GoogleAppDriveOAuthRules.ConnectScopes);
        Assert.DoesNotContain(GoogleCalendarOAuthRules.CalendarScope, GoogleAppDriveOAuthRules.ConnectScopes);
    }

    [Fact]
    public void Connect_scopes_include_email_for_status_display()
    {
        Assert.Contains(GoogleCalendarOAuthRules.EmailScope, GoogleAppDriveOAuthRules.ConnectScopes);
        Assert.Contains(GoogleCalendarOAuthRules.OpenIdScope, GoogleAppDriveOAuthRules.ConnectScopes);
    }

    [Fact]
    public void Token_refresh_does_not_send_intranet_file_scopes()
    {
        Assert.Empty(GoogleAppDriveOAuthRules.TokenRefreshScopes);
        Assert.DoesNotContain(GoogleDriveOAuthRules.DriveFileScope, GoogleAppDriveOAuthRules.TokenRefreshScopes);
        Assert.DoesNotContain(GoogleDriveOAuthRules.DriveReadonlyScope, GoogleAppDriveOAuthRules.TokenRefreshScopes);
    }
}
