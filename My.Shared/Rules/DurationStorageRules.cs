namespace My.Shared.Rules;

/// <summary>
/// SQL Server maps <see cref="TimeSpan"/> Duration columns as <c>time</c>, which only
/// stores values in <c>[00:00:00, 24:00:00)</c>. Values of 24h or more throw
/// <c>SqlDbType.Time overflow</c> on insert/update — which must never reach the database.
/// Client UI and FluentValidation should reject earlier with the same limit.
/// </summary>
public static class DurationStorageRules
{
    /// <summary>
    /// Largest duration we will store (minute precision). Strictly less than 24 hours.
    /// </summary>
    public static readonly TimeSpan MaxStoredDuration = new(23, 59, 0);

    /// <summary>Max whole hours allowed in timed-entry hour fields (dialog numeric input).</summary>
    public const int MaxHoursComponent = 23;

    public static bool IsWithinStorageLimit(TimeSpan duration) =>
        duration >= TimeSpan.Zero && duration <= MaxStoredDuration;

    public const string ExceedsStorageLimitMessage =
        "Duration cannot exceed 23 hours 59 minutes. Split longer work across multiple days or tasks.";

    /// <summary>
    /// Null when OK; otherwise a user-facing message. Zero is allowed (active timer / empty cell).
    /// </summary>
    public static string? ValidateForStorage(TimeSpan duration)
    {
        if (duration < TimeSpan.Zero)
            return "Duration cannot be negative.";
        if (duration > MaxStoredDuration)
            return ExceedsStorageLimitMessage;
        return null;
    }

    public const string ZeroDurationMessage = "Enter a duration greater than zero.";

    /// <summary>
    /// Null when OK; otherwise a user-facing message. Unlike <see cref="ValidateForStorage"/>
    /// (which allows zero for an in-progress stopwatch or an empty grid cell), this is the
    /// stricter check applied when a task is actually being <em>saved</em> as a finished
    /// entry — a timed entry with zero duration on save is meaningless and previously slipped
    /// through because new tasks defaulted to 30 minutes and nobody noticed a blank save.
    /// All-day entries derive their duration from workday hours
    /// (<see cref="AllDayEntryRules.DurationFor"/>), not this field, so they're exempt.
    /// </summary>
    public static string? ValidateForFinalize(TimeSpan duration, bool isAllDay)
    {
        if (isAllDay) return null;
        if (duration <= TimeSpan.Zero) return ZeroDurationMessage;
        return ValidateForStorage(duration);
    }
}
