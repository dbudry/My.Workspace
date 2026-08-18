using System.Globalization;

namespace My.Shared.Rules;

/// <summary>
/// Formats a TrackedTask's start/end DateTime into the (dateTime, timeZone)
/// pair Google Calendar's API expects.
///
/// Timed entries are stored as UTC. When the user has an IANA timezone, convert
/// that instant to wall clock in their zone and send the naked local string plus
/// the zone so Google places the event at the correct moment (including DST).
/// </summary>
public static class GoogleEventTimeRules
{
    /// <summary>
    /// Returns the (dateTimeRaw, timeZone) pair for an <c>EventDateTime</c>:
    /// <list type="bullet">
    ///   <item>If <paramref name="timeZone"/> is a non-empty IANA/Windows id, treats
    ///     <paramref name="utcOrUnspecified"/> as a UTC instant, converts to that zone's
    ///     wall clock, and returns the naked local string + the timezone id.</item>
    ///   <item>If <paramref name="timeZone"/> is null/empty, stamps as UTC with a +00:00
    ///     offset (legacy fallback when the user has not set a timezone).</item>
    /// </list>
    /// </summary>
    public static (string DateTimeRaw, string? TimeZone) FormatForGoogle(DateTime utcOrUnspecified, string? timeZone)
    {
        if (string.IsNullOrWhiteSpace(timeZone))
        {
            var asUtc = DateTime.SpecifyKind(
                utcOrUnspecified.Kind == DateTimeKind.Local
                    ? utcOrUnspecified.ToUniversalTime()
                    : utcOrUnspecified,
                DateTimeKind.Utc);
            return (new DateTimeOffset(asUtc).ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture), null);
        }

        var utc = utcOrUnspecified.Kind switch
        {
            DateTimeKind.Utc => utcOrUnspecified,
            DateTimeKind.Local => utcOrUnspecified.ToUniversalTime(),
            _ => DateTime.SpecifyKind(utcOrUnspecified, DateTimeKind.Utc),
        };

        var tz = UserTimeZoneRules.Resolve(timeZone);
        var wall = TimeZoneInfo.ConvertTimeFromUtc(utc, tz);
        var raw = wall.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
        return (raw, timeZone);
    }
}
