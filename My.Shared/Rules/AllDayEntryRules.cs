using System.Globalization;

namespace My.Shared.Rules;

/// <summary>
/// Rules for "all-day" tracked tasks (Vacation, OOO, half-day Sick blocks shown as
/// full days, etc.). All-day entries don't ship with hours typed by the user — instead
/// the system derives <c>Duration</c> from a workspace-wide <c>WorkdayHours</c> setting
/// multiplied by the calendar-day span. That way existing report code that sums
/// <c>Duration</c> keeps working: a 5-day vacation contributes 5 × workday hours.
/// </summary>
public static class AllDayEntryRules
{
    /// <summary>Fallback used when the workspace setting is missing or unparseable.</summary>
    public const double DefaultWorkdayHours = 8.0;

    /// <summary>Hard floor on what we'll accept — pathological values should not eat reports.</summary>
    public const double MinWorkdayHours = 0.5;

    /// <summary>Hard ceiling — same idea.</summary>
    public const double MaxWorkdayHours = 24.0;

    /// <summary>
    /// Longest all-day span we accept (inclusive calendar days). A week of vacation is
    /// fine; a multi-month block should be split so reports stay readable.
    /// </summary>
    public const int MaxInclusiveCalendarDays = 90;

    public const string SpanTooLongMessage =
        "All-day span cannot exceed 90 calendar days. Split longer time off into separate entries.";

    /// <summary>
    /// Parses the AppSetting string into a usable hours-per-day double. Bad input
    /// (empty, non-numeric, NaN, negative, &gt;24) falls back to <see cref="DefaultWorkdayHours"/>.
    /// Always returns a value in <c>[MinWorkdayHours, MaxWorkdayHours]</c>.
    /// </summary>
    public static double ParseWorkdayHours(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return DefaultWorkdayHours;

        if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var hours))
            return DefaultWorkdayHours;

        if (double.IsNaN(hours) || double.IsInfinity(hours))
            return DefaultWorkdayHours;

        if (hours < MinWorkdayHours) return MinWorkdayHours;
        if (hours > MaxWorkdayHours) return MaxWorkdayHours;
        return hours;
    }

    /// <summary>
    /// Returns the inclusive number of calendar days between <paramref name="start"/> and
    /// <paramref name="end"/>. A single-day entry is 1 day; a Mon→Fri span is 5 days. If
    /// <paramref name="end"/> is null or earlier than <paramref name="start"/>, returns 1.
    /// Time-of-day is ignored — only the calendar date matters. Used by the Google
    /// all-day event renderer; for PTO duration math use <see cref="WorkdaysInSpan"/>.
    /// </summary>
    public static int InclusiveDaySpan(DateTime start, DateTime? end)
    {
        if (!end.HasValue) return 1;
        var startDay = start.Date;
        var endDay = end.Value.Date;
        if (endDay < startDay) return 1;
        return (int)(endDay - startDay).TotalDays + 1;
    }

    /// <summary>
    /// Returns the inclusive number of <em>workdays</em> (Monday–Friday) between
    /// <paramref name="start"/> and <paramref name="end"/>. A Saturday-only entry is 0
    /// workdays; Mon→Fri is 5; Mon→next Mon is 6. Null or inverted ranges count just
    /// <paramref name="start"/>'s day (0 if start is itself a weekend).
    /// Weekend hours are excluded so a Friday-through-next-Wednesday vacation logs the
    /// right four workdays (Fri, Mon, Tue, Wed) instead of seven calendar days.
    /// </summary>
    public static int WorkdaysInSpan(DateTime start, DateTime? end)
    {
        var startDay = start.Date;
        var lastDay = (end?.Date) ?? startDay;
        if (lastDay < startDay) lastDay = startDay;

        var count = 0;
        for (var d = startDay; d <= lastDay; d = d.AddDays(1))
        {
            if (d.DayOfWeek != DayOfWeek.Saturday && d.DayOfWeek != DayOfWeek.Sunday)
                count++;
        }
        return count;
    }

    /// <summary>
    /// Derived <c>Duration</c> for an all-day entry: <c>workdayHours × WorkdaysInSpan</c>.
    /// Weekend days don't accrue PTO so a Fri→Mon vacation logs 2 workdays, not 4 calendar
    /// days. Reports and dashboards consume the resulting Duration unchanged.
    /// A Tue–Thu span at 8h/day is 24 hours. SQL <c>time</c> cannot store that, so
    /// persist via <see cref="DurationStorageRules.ForSqlTimeColumn"/> and read the
    /// real total with <see cref="EffectiveDuration"/>.
    /// </summary>
    public static TimeSpan DurationFor(DateTime start, DateTime? end, double workdayHours) =>
        TimeSpan.FromHours(workdayHours * WorkdaysInSpan(start, end));

    /// <summary>
    /// Timed entries use the stored <c>time</c> value (including seconds). All-day
    /// entries always recompute from dates so a 24h vacation is not lost.
    /// </summary>
    public static TimeSpan EffectiveDuration(
        bool isAllDay,
        DateTime start,
        DateTime? end,
        TimeSpan stored,
        double workdayHours)
        => isAllDay ? DurationFor(start, end, workdayHours) : stored;

    /// <summary>
    /// Null when the inclusive calendar span is acceptable; otherwise a user-facing message.
    /// </summary>
    public static string? ValidateSpan(DateTime start, DateTime? end)
    {
        if (InclusiveDaySpan(start, end) > MaxInclusiveCalendarDays)
            return SpanTooLongMessage;
        return null;
    }

    /// <summary>
    /// Returns the (startDate, endDateExclusive) string pair for Google Calendar's
    /// <c>EventDateTime.Date</c> shape. Google treats <c>end.date</c> as exclusive,
    /// so a Mon→Fri vacation is start=Mon, end=Sat. Format is <c>yyyy-MM-dd</c>.
    /// </summary>
    public static (string StartDate, string EndDateExclusive) FormatAllDayForGoogle(DateTime start, DateTime? end)
    {
        var startDay = start.Date;
        var lastDay = end?.Date ?? startDay;
        if (lastDay < startDay) lastDay = startDay;

        var endExclusive = lastDay.AddDays(1);

        return (
            startDay.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            endExclusive.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
    }
}
