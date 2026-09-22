using My.Shared.Rules;
using Xunit;

namespace My.Tests.Rules;

public class TeamAvailabilityHoursRulesTests
{
    [Fact]
    public void Missing_flag_counts_as_time()
    {
        Assert.True(TeamAvailabilityHoursRules.CountsTowardTyme(null));
        Assert.True(TeamAvailabilityHoursRules.CountsTowardTyme(true));
        Assert.False(TeamAvailabilityHoursRules.CountsTowardTyme(false));
    }

    [Fact]
    public void Presence_only_requires_shared_availability_with_hours_off()
    {
        Assert.False(TeamAvailabilityHoursRules.IsPresenceOnly(false, false));
        Assert.False(TeamAvailabilityHoursRules.IsPresenceOnly(true, true));
        Assert.True(TeamAvailabilityHoursRules.IsPresenceOnly(true, false));
    }

    [Fact]
    public void HoursFor_zeros_presence_only_including_all_day()
    {
        var start = new DateTime(2026, 9, 21);
        var stored = TimeSpan.FromHours(8);
        Assert.Equal(TimeSpan.Zero, TeamAvailabilityHoursRules.HoursFor(
            false, isAllDay: true, start, start, stored, 8));
        Assert.Equal(TimeSpan.Zero, TeamAvailabilityHoursRules.HoursFor(
            false, isAllDay: false, start, start.AddHours(2), TimeSpan.FromHours(2), 8));
    }

    [Fact]
    public void HoursFor_all_day_time_off_still_derives_workday_hours()
    {
        var start = new DateTime(2026, 9, 21); // Monday
        Assert.Equal(TimeSpan.FromHours(8), TeamAvailabilityHoursRules.HoursFor(
            true, isAllDay: true, start, start, TimeSpan.Zero, 8));
    }

    [Fact]
    public void DisplayDuration_keeps_timed_busy_block_length()
    {
        var start = new DateTime(2026, 9, 21, 13, 0, 0);
        var stored = TimeSpan.FromHours(2);
        Assert.Equal(stored, TeamAvailabilityHoursRules.DisplayDuration(
            false, isAllDay: false, start, start.AddHours(2), stored, 8));
    }

    [Fact]
    public void DisplayDuration_zeros_all_day_presence_only()
    {
        var start = new DateTime(2026, 9, 21);
        Assert.Equal(TimeSpan.Zero, TeamAvailabilityHoursRules.DisplayDuration(
            false, isAllDay: true, start, start, TimeSpan.Zero, 8));
    }
}
