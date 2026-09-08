using My.Shared.Rules;
using Xunit;

namespace My.Tests.Rules;

public class WeeklyTimeTotalsRulesTests
{
    private static readonly DateTime Mon = new(2026, 6, 22);
    private static readonly DateTime Sun = new(2026, 6, 28);

    [Fact]
    public void TheirTime_sums_originals_only_single_total()
    {
        var tasks = new[]
        {
            Slice(Mon, hours: 8),
            Slice(Mon.AddDays(1), hours: 8, adjustedHours: 7.5)
        };

        var r = WeeklyTimeTotalsRules.Compute(tasks, Mon, Sun, EmployeeTimeDisplayMode.TheirTime);
        Assert.False(r.ShowAdjustedSeparately);
        Assert.Equal(TimeSpan.FromHours(16), r.PrimaryTotal);
        Assert.Equal(TimeSpan.FromHours(16), r.OriginalTotal);
    }

    [Fact]
    public void Adjusted_uses_correction_when_present_else_original()
    {
        var tasks = new[]
        {
            Slice(Mon, hours: 8),
            Slice(Mon.AddDays(1), hours: 8, adjustedHours: 7.5)
        };

        var r = WeeklyTimeTotalsRules.Compute(tasks, Mon, Sun, EmployeeTimeDisplayMode.Adjusted);
        Assert.False(r.ShowAdjustedSeparately);
        Assert.Equal(TimeSpan.FromHours(15.5), r.PrimaryTotal);
    }

    [Fact]
    public void Both_with_adjustments_shows_two_totals()
    {
        var tasks = new[]
        {
            Slice(Mon, hours: 8),
            Slice(Mon.AddDays(1), hours: 8),
            Slice(Mon.AddDays(2), hours: 8),
            Slice(Mon.AddDays(3), hours: 8),
            Slice(Mon.AddDays(4), hours: 8, adjustedHours: 7.5)
        };

        var r = WeeklyTimeTotalsRules.Compute(tasks, Mon, Sun, EmployeeTimeDisplayMode.Both);
        Assert.True(r.ShowAdjustedSeparately);
        Assert.Equal(TimeSpan.FromHours(40), r.OriginalTotal);
        Assert.Equal(TimeSpan.FromHours(39.5), r.AdjustedTotal);
    }

    [Fact]
    public void Both_without_adjustments_is_single_total()
    {
        var tasks = new[]
        {
            Slice(Mon, hours: 8),
            Slice(Mon.AddDays(1), hours: 8)
        };

        var r = WeeklyTimeTotalsRules.Compute(tasks, Mon, Sun, EmployeeTimeDisplayMode.Both);
        Assert.False(r.ShowAdjustedSeparately);
        Assert.Equal(TimeSpan.FromHours(16), r.PrimaryTotal);
        Assert.Equal(r.OriginalTotal, r.AdjustedTotal);
    }

    [Fact]
    public void All_day_multi_day_is_prorated_to_workdays_in_the_week()
    {
        // Jen's Africa OOO: Sep 2–14 (9 × 8h = 72h). Week of Aug 31 only has Wed–Fri.
        var tasks = new[]
        {
            new WeeklyTimeTotalsRules.TaskDurationSlice(
                new DateTime(2026, 9, 2),
                TimeSpan.FromHours(72),
                AdjustedDuration: null,
                IsAllDay: true,
                EndDate: new DateTime(2026, 9, 14))
        };

        var r = WeeklyTimeTotalsRules.Compute(
            tasks, new DateTime(2026, 8, 31), new DateTime(2026, 9, 6),
            EmployeeTimeDisplayMode.TheirTime);
        Assert.Equal(TimeSpan.FromHours(24), r.PrimaryTotal);
    }

    [Fact]
    public void Outside_week_is_excluded()
    {
        var tasks = new[]
        {
            Slice(Mon, hours: 8),
            Slice(Mon.AddDays(10), hours: 8, adjustedHours: 1)
        };

        var r = WeeklyTimeTotalsRules.Compute(tasks, Mon, Sun, EmployeeTimeDisplayMode.Both);
        Assert.False(r.ShowAdjustedSeparately);
        Assert.Equal(TimeSpan.FromHours(8), r.PrimaryTotal);
    }

    private static WeeklyTimeTotalsRules.TaskDurationSlice Slice(
        DateTime start,
        double hours,
        double? adjustedHours = null) =>
        new(
            start,
            TimeSpan.FromHours(hours),
            adjustedHours.HasValue ? TimeSpan.FromHours(adjustedHours.Value) : null);
}
