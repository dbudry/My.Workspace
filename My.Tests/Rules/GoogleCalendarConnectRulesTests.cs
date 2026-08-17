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
}
