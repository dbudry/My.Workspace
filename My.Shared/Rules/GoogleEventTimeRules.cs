using System.Globalization;

namespace My.Shared.Rules;

/// <summary>
/// Formats a TrackedTask's start/end DateTime into the (dateTime, timeZone)
/// pair Google Calendar's API expects.
///
/// Why a helper: the old <c>BuildEvent</c> took a wall-clock DateTime (Kind=Unspecified
/// because it came out of EF) and stamped it as UTC via <c>DateTime.SpecifyKind</c>.
/// That's a lie — a 9 AM Eastern task got pushed as 9 AM UTC, landing on Google at
/// 5 AM Eastern. Derek created a "Test My Primary Cal" task today and didn't see it
/// because it was 4 hours earlier than expected. Fix: send the wall-clock string
/// plus the user's IANA timezone and let Google interpret the moment, no DST math
/// on our side.
/// </summary>
public static class GoogleEventTimeRules
{
    /// <summary>
    /// Returns the (dateTimeRaw, timeZone) pair for an <c>EventDateTime</c>:
    /// <list type="bullet">
    ///   <item>If <paramref name="timeZone"/> is a non-empty IANA name, returns the
    ///     wall-clock string (no offset suffix) plus the timezone. Google reads this
    ///     as "9 AM in this zone" regardless of DST.</item>
    ///   <item>If <paramref name="timeZone"/> is null/empty, falls back to the legacy
    ///     behavior: stamp the DateTime as UTC and emit it with a +00:00 offset.
    ///     Only correct if the moment really was in UTC; intended as a safe-ish
    ///     default for users who haven't set their timezone yet.</item>
    /// </list>
    /// </summary>
    public static (string DateTimeRaw, string? TimeZone) FormatForGoogle(DateTime localOrUtc, string? timeZone)
    {
        if (string.IsNullOrWhiteSpace(timeZone))
        {
            var asUtc = DateTime.SpecifyKind(localOrUtc, DateTimeKind.Utc);
            return (new DateTimeOffset(asUtc).ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture), null);
        }

        var raw = localOrUtc.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
        return (raw, timeZone);
    }
}
