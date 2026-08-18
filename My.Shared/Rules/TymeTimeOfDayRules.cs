namespace My.Shared.Rules;

/// <summary>
/// Workspace flag: whether Tyme entry surfaces collect and display start time of day.
/// Calendar sync and stopwatch sessions still use real timestamps regardless.
/// </summary>
public static class TymeTimeOfDayRules
{
    /// <summary>Default when the setting is missing — keep full time-of-day UX.</summary>
    public const bool DefaultTrackTimeOfDay = true;

    /// <summary>
    /// Parses AppSettings value. Missing/empty/unparseable → <see cref="DefaultTrackTimeOfDay"/>.
    /// Accepts true/false, 1/0, yes/no (case-insensitive).
    /// </summary>
    public static bool ParseTrackTimeOfDay(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return DefaultTrackTimeOfDay;

        var s = raw.Trim();
        if (bool.TryParse(s, out var b))
            return b;
        if (string.Equals(s, "1", StringComparison.OrdinalIgnoreCase)
            || string.Equals(s, "yes", StringComparison.OrdinalIgnoreCase)
            || string.Equals(s, "y", StringComparison.OrdinalIgnoreCase)
            || string.Equals(s, "on", StringComparison.OrdinalIgnoreCase))
            return true;
        if (string.Equals(s, "0", StringComparison.OrdinalIgnoreCase)
            || string.Equals(s, "no", StringComparison.OrdinalIgnoreCase)
            || string.Equals(s, "n", StringComparison.OrdinalIgnoreCase)
            || string.Equals(s, "off", StringComparison.OrdinalIgnoreCase))
            return false;

        return DefaultTrackTimeOfDay;
    }

    /// <summary>
    /// Wall-clock start when time-of-day is not tracked: first instant of the calendar day
    /// in the user's zone (midnight). Stored as UTC via the normal convert path.
    /// </summary>
    public static TimeSpan DefaultStartTimeOfDayWhenNotTracked => TimeSpan.Zero;
}
