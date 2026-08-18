using My.Shared.Rules;
using Xunit;

namespace My.Tests.Rules;

public class CalendarDayTotalRulesTests
{
    [Fact]
    public void Timed_chips_sum_on_their_start_day()
    {
        var chips = new[]
        {
            new CalendarDayTotalRules.Chip(new DateTime(2026, 8, 13, 8, 0, 0), TimeSpan.FromHours(2), false, false, "a"),
            new CalendarDayTotalRules.Chip(new DateTime(2026, 8, 13, 13, 0, 0), TimeSpan.FromHours(1.5), false, false, "b"),
            new CalendarDayTotalRules.Chip(new DateTime(2026, 8, 14, 9, 0, 0), TimeSpan.FromHours(3), false, false, "c")
        };

        var totals = CalendarDayTotalRules.SumByDay(chips, 8);
        Assert.Equal(TimeSpan.FromHours(3.5), totals[new DateOnly(2026, 8, 13)]);
        Assert.Equal(TimeSpan.FromHours(3), totals[new DateOnly(2026, 8, 14)]);
    }

    [Fact]
    public void Overlay_is_skipped_when_original_is_also_on_the_day()
    {
        var chips = new[]
        {
            new CalendarDayTotalRules.Chip(new DateTime(2026, 8, 13, 8, 0, 0), TimeSpan.FromHours(8), false, false, "t1"),
            new CalendarDayTotalRules.Chip(new DateTime(2026, 8, 13, 8, 0, 0), TimeSpan.FromHours(6), false, true, "t1")
        };

        var totals = CalendarDayTotalRules.SumByDay(chips, 8);
        Assert.Equal(TimeSpan.FromHours(8), totals[new DateOnly(2026, 8, 13)]);
    }

    [Fact]
    public void Overlay_counts_when_original_is_not_shown()
    {
        var chips = new[]
        {
            new CalendarDayTotalRules.Chip(new DateTime(2026, 8, 13, 8, 0, 0), TimeSpan.FromHours(6), false, true, "t1")
        };

        var totals = CalendarDayTotalRules.SumByDay(chips, 8);
        Assert.Equal(TimeSpan.FromHours(6), totals[new DateOnly(2026, 8, 13)]);
    }

    [Fact]
    public void All_day_weekday_uses_workday_hours_not_full_span_duration()
    {
        var chips = new[]
        {
            new CalendarDayTotalRules.Chip(new DateTime(2026, 8, 13), TimeSpan.FromHours(24), true, false, "ooo")
        };

        var totals = CalendarDayTotalRules.SumByDay(chips, 8);
        Assert.Equal(TimeSpan.FromHours(8), totals[new DateOnly(2026, 8, 13)]);
    }

    [Fact]
    public void All_day_weekend_contributes_zero()
    {
        var chips = new[]
        {
            new CalendarDayTotalRules.Chip(new DateTime(2026, 8, 15), TimeSpan.FromHours(8), true, false, "ooo") // Saturday
        };

        var totals = CalendarDayTotalRules.SumByDay(chips, 8);
        Assert.Empty(totals);
    }

    [Fact]
    public void Format_matches_week_grid_style()
    {
        Assert.Equal("8h", CalendarDayTotalRules.Format(TimeSpan.FromHours(8)));
        Assert.Equal("1h 30m", CalendarDayTotalRules.Format(TimeSpan.FromMinutes(90)));
    }
}
