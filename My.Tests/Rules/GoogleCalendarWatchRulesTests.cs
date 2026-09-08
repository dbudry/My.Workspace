using My.Shared.Rules;
using Xunit;

namespace My.Tests.Rules;

public class GoogleCalendarWatchRulesTests
{
    [Fact]
    public void Live_watch_requires_channel_and_future_expiry()
    {
        var now = new DateTime(2026, 9, 2, 12, 0, 0, DateTimeKind.Utc);
        Assert.True(GoogleCalendarWatchRules.IsLiveWatchActive("ch-1", now.AddHours(1), now));
        Assert.False(GoogleCalendarWatchRules.IsLiveWatchActive("ch-1", now.AddMinutes(-1), now));
        Assert.False(GoogleCalendarWatchRules.IsLiveWatchActive(null, now.AddHours(1), now));
        Assert.False(GoogleCalendarWatchRules.IsLiveWatchActive("ch-1", null, now));
        Assert.False(GoogleCalendarWatchRules.IsLiveWatchActive("", now.AddHours(1), now));
    }
}
