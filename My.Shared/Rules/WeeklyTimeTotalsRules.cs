namespace My.Shared.Rules;

/// <summary>
/// Week totals for Tasks → Weekly, respecting employee time display mode
/// (original vs manager-adjusted). Pure logic for unit tests.
/// </summary>
public static class WeeklyTimeTotalsRules
{
    /// <summary>One task's contribution in a week window.</summary>
    public readonly record struct TaskDurationSlice(
        DateTime StartDate,
        TimeSpan OriginalDuration,
        /// <summary>Null when there is no manager Alias/Direct correction.</summary>
        TimeSpan? AdjustedDuration,
        bool IsAllDay = false,
        DateTime? EndDate = null);

    /// <summary>
    /// <see cref="ShowAdjustedSeparately"/> is true only in <see cref="EmployeeTimeDisplayMode.Both"/>
    /// when at least one task in range has a correction — UI then shows two totals.
    /// Otherwise <see cref="PrimaryTotal"/> is the single number to display.
    /// </summary>
    public readonly record struct Result(
        TimeSpan PrimaryTotal,
        TimeSpan OriginalTotal,
        TimeSpan AdjustedTotal,
        bool ShowAdjustedSeparately);

    /// <summary>
    /// Sums original and adjusted week totals. Timed rows count when their start
    /// date is in [from, to]. All-day rows count the workdays that overlap the
    /// window (a multi-day OOO is not dumped entirely into the week it starts).
    /// </summary>
    public static Result Compute(
        IEnumerable<TaskDurationSlice> tasks,
        DateTime from,
        DateTime to,
        EmployeeTimeDisplayMode displayMode)
    {
        var f = from.Date;
        var end = to.Date;
        var original = TimeSpan.Zero;
        var adjusted = TimeSpan.Zero;
        var anyAdjustment = false;

        foreach (var t in tasks)
        {
            TimeSpan orig;
            TimeSpan adj;
            if (t.IsAllDay)
            {
                orig = Normalize(AllDayEntryRules.ProrateDurationForWindow(
                    t.StartDate, t.EndDate, f, end, t.OriginalDuration));
                if (orig <= TimeSpan.Zero)
                    continue;

                if (t.AdjustedDuration.HasValue)
                {
                    anyAdjustment = true;
                    adj = Normalize(AllDayEntryRules.ProrateDurationForWindow(
                        t.StartDate, t.EndDate, f, end, t.AdjustedDuration.Value));
                }
                else
                {
                    adj = orig;
                }
            }
            else
            {
                var sd = t.StartDate.Date;
                if (sd < f || sd > end)
                    continue;

                orig = Normalize(t.OriginalDuration);
                if (t.AdjustedDuration.HasValue)
                {
                    anyAdjustment = true;
                    adj = Normalize(t.AdjustedDuration.Value);
                }
                else
                {
                    adj = orig;
                }
            }

            original += orig;
            adjusted += adj;
        }

        original = Normalize(original);
        adjusted = Normalize(adjusted);

        return displayMode switch
        {
            EmployeeTimeDisplayMode.TheirTime =>
                new Result(original, original, adjusted, ShowAdjustedSeparately: false),

            EmployeeTimeDisplayMode.Adjusted =>
                new Result(adjusted, original, adjusted, ShowAdjustedSeparately: false),

            _ => // Both
                new Result(
                    PrimaryTotal: original,
                    OriginalTotal: original,
                    AdjustedTotal: adjusted,
                    ShowAdjustedSeparately: anyAdjustment)
        };
    }

    private static TimeSpan Normalize(TimeSpan d)
    {
        if (d < TimeSpan.Zero) return TimeSpan.Zero;
        return new TimeSpan((int)d.TotalHours, d.Minutes, 0);
    }
}
