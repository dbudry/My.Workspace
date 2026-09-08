using My.Shared.Rules;
using Xunit;

namespace My.Tests.Rules;

public class GoogleCalendarConnectRulesTests
{
    [Fact]
    public void First_time_user_auto_connects() =>
        Assert.True(GoogleCalendarConnectRules.ShouldAutoConnectOnLogin(
            isCalendarConnected: false, autoConnectOptOut: false));

    [Fact]
    public void After_disconnect_login_does_not_auto_connect() =>
        Assert.False(GoogleCalendarConnectRules.ShouldAutoConnectOnLogin(
            isCalendarConnected: false, autoConnectOptOut: true));

    [Fact]
    public void Already_connected_does_not_start_oauth_again() =>
        Assert.False(GoogleCalendarConnectRules.ShouldAutoConnectOnLogin(
            isCalendarConnected: true, autoConnectOptOut: false));

    [Fact]
    public void Oauth_redirect_without_error_is_null() =>
        Assert.Null(GoogleCalendarConnectRules.ExplainOauthRedirectError(null, null));

    [Fact]
    public void Oauth_access_denied_is_plain_language() =>
        Assert.Contains("declined", GoogleCalendarConnectRules.ExplainOauthRedirectError("access_denied", null),
            StringComparison.OrdinalIgnoreCase);

    [Fact]
    public void Oauth_backend_error_asks_to_retry()
    {
        var text = GoogleCalendarConnectRules.ExplainOauthRedirectError("server_error", "Backend Error");
        Assert.Contains("connect again", text, StringComparison.OrdinalIgnoreCase);
    }
}
