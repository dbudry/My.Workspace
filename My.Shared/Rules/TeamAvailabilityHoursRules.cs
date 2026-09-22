namespace My.Shared.Rules;

/// <summary>
/// Hour-total rules for Team Availability categories. Time-off projects
/// (Vacation, Sick) count as Tyme hours. Presence-only projects (Unavailable,
/// Busy) still dual-publish to the team calendar but contribute zero hours.
/// </summary>
public static class TeamAvailabilityHoursRules
{
    /// <summary>
    /// True unless this is an availability category the manager marked as
    /// calendar-only. Null/missing (no project loaded) counts as time.
    /// </summary>
    public static bool CountsTowardTyme(bool? countsAsTime) => countsAsTime != false;

    /// <summary>
    /// Presence-only is valid only on a shared-availability project with hours off.
    /// </summary>
    public static bool IsPresenceOnly(bool isSharedAvailability, bool countsAsTime) =>
        isSharedAvailability && !countsAsTime;

    /// <summary>
    /// Duration that contributes to reports, dashboard, Submit, and calendar
    /// footers. Presence-only is always zero, including all-day spans that
    /// would otherwise derive workday hours.
    /// </summary>
    public static TimeSpan HoursFor(
        bool? countsAsTime,
        bool isAllDay,
        DateTime start,
        DateTime? end,
        TimeSpan stored,
        double workdayHours)
        => CountsTowardTyme(countsAsTime)
            ? AllDayEntryRules.EffectiveDuration(isAllDay, start, end, stored, workdayHours)
            : TimeSpan.Zero;

    /// <summary>
    /// Duration shown on the task itself. Timed presence-only keeps the busy-block
    /// length (calendar span / edit dialog). All-day presence-only is zero so lists
    /// do not show derived workday hours.
    /// </summary>
    public static TimeSpan DisplayDuration(
        bool? countsAsTime,
        bool isAllDay,
        DateTime start,
        DateTime? end,
        TimeSpan stored,
        double workdayHours)
    {
        if (!CountsTowardTyme(countsAsTime) && isAllDay)
            return TimeSpan.Zero;
        return AllDayEntryRules.EffectiveDuration(isAllDay, start, end, stored, workdayHours);
    }
}
