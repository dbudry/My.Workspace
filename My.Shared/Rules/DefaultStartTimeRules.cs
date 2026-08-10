namespace My.Shared.Rules;

/// <summary>
/// Per-user wall-clock default start time for new Tyme entries (dialog, day grid, etc.).
/// Stored as minutes past midnight in the user's timezone (0–1439).
/// </summary>
public static class DefaultStartTimeRules
{
    /// <summary>Product default: 08:00 local when the user has not chosen a value.</summary>
    public const int DefaultMinutesPastMidnight = 8 * 60;

    public static readonly TimeSpan DefaultTimeOfDay = TimeSpan.FromMinutes(DefaultMinutesPastMidnight);

    /// <summary>Clamp minutes into a valid time-of-day (0:00–23:59).</summary>
    public static int ClampMinutes(int minutes)
    {
        if (minutes < 0) return 0;
        if (minutes > 23 * 60 + 59) return 23 * 60 + 59;
        return minutes;
    }

    /// <summary>
    /// Resolve stored minutes (or null/missing) to a <see cref="TimeSpan"/> for StartDate.TimeOfDay.
    /// Null/negative treated as the product default (08:00).
    /// </summary>
    public static TimeSpan Resolve(int? minutesPastMidnight)
    {
        if (!minutesPastMidnight.HasValue || minutesPastMidnight.Value < 0)
            return DefaultTimeOfDay;
        return TimeSpan.FromMinutes(ClampMinutes(minutesPastMidnight.Value));
    }

    public static int FromTimeSpan(TimeSpan timeOfDay)
    {
        if (timeOfDay < TimeSpan.Zero) return 0;
        var total = (int)timeOfDay.TotalMinutes;
        return ClampMinutes(total);
    }
}
