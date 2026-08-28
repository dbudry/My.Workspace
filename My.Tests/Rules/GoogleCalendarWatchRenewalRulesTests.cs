using My.Shared.Rules;
using Xunit;

namespace My.Tests.Rules;

public class GoogleCalendarWatchRenewalRulesTests
{
    private static readonly DateTime Now = new(2026, 8, 27, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void NeedsRenewal_when_never_watched() =>
        Assert.True(GoogleCalendarWatchRenewalRules.NeedsRenewal(null, Now));

    [Fact]
    public void NeedsRenewal_when_already_expired() =>
        Assert.True(GoogleCalendarWatchRenewalRules.NeedsRenewal(Now.AddDays(-3), Now));

    [Fact]
    public void NeedsRenewal_when_inside_96h_window() =>
        Assert.True(GoogleCalendarWatchRenewalRules.NeedsRenewal(Now.AddHours(95), Now));

    [Fact]
    public void Does_not_renew_when_expiry_is_beyond_window() =>
        Assert.False(GoogleCalendarWatchRenewalRules.NeedsRenewal(Now.AddHours(97), Now));
}
