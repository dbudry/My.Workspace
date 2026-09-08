using My.Shared.Rules;
using Xunit;

namespace My.Tests.Rules;

public class GoogleCalendarAvailabilitySyncRulesTests
{
    [Theory]
    [InlineData(false, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    public void AllowsPersonalSync_limits_to_availability_when_opted_in(
        bool availabilityOnly, bool isShared, bool expected) =>
        Assert.Equal(expected, GoogleCalendarAvailabilitySyncRules.AllowsPersonalSync(availabilityOnly, isShared));

    [Fact]
    public void Publish_requires_export_toggle() =>
        Assert.False(GoogleCalendarAvailabilitySyncRules.ShouldPublishToPersonalCalendar(
            publishEnabled: false, availabilityOnly: false, projectIsSharedAvailability: true));

    [Theory]
    [InlineData(false, false, false, false)]
    [InlineData(false, true, false, false)]
    [InlineData(false, true, true, false)]
    [InlineData(true, false, false, false)]
    [InlineData(true, true, true, false)]
    [InlineData(true, true, false, true)]
    public void Unlink_only_when_export_is_on_and_availability_only_excludes_the_project(
        bool publishEnabled, bool availabilityOnly, bool isShared, bool expected) =>
        Assert.Equal(expected, GoogleCalendarAvailabilitySyncRules.ShouldUnlinkPersonalEvent(
            publishEnabled, availabilityOnly, isShared));
}
