using System.Globalization;

namespace My.Shared.Rules;

/// <summary>
/// Parses the start/end of a Google Calendar event into Tyme's storage convention.
/// Google sends events in one of two shapes:
///
/// 1. Timed events: <c>DateTimeDateTimeOffset</c> populated with a real UTC moment.
/// 2. All-day events: <c>Date</c> populated with a <c>yyyy-MM-dd</c> string. End is
///    EXCLUSIVE — a Mon→Fri vacation arrives as <c>start=Mon, end=Sat</c>.
///
/// Tyme stores both as DateTime values tagged <see cref="DateTimeKind.Utc"/>. For
/// timed events the caller is expected to convert UTC → user's local zone before
/// passing here (see <c>GoogleCalendarFunction.ConvertUtcToLocalWallClock</c>) so the
/// "wall-clock pretending to be UTC" convention matches dialog-created tasks. For
/// all-day, the date is just the date — no zone math.
///
/// Extracted from <c>GoogleCalendarFunction.ImportChangesAsync</c> so the parsing
/// logic is unit-testable without spinning up the Google SDK or a DbContext.
/// </summary>
public static class CalendarEventDateRules
{
    public record ParsedDates(DateTime StartDate, DateTime EndDate, bool IsAllDay);

    /// <summary>
    /// Parses an event's date payload. Returns null when neither shape is present
    /// (caller should skip the event with a log line).
    /// </summary>
    /// <param name="timedStartUtc">
    /// <c>ev.Start.DateTimeDateTimeOffset?.UtcDateTime</c>. Null when the event is
    /// all-day; populated for timed events.
    /// </param>
    /// <param name="timedEndUtc">
    /// <c>ev.End.DateTimeDateTimeOffset?.UtcDateTime</c>. Null when the event is
    /// all-day.
    /// </param>
    /// <param name="allDayStartString">
    /// <c>ev.Start.Date</c> in <c>yyyy-MM-dd</c> form. Null/empty when the event is
    /// timed. Google's convention: this is the literal start day.
    /// </param>
    /// <param name="allDayEndString">
    /// <c>ev.End.Date</c> in <c>yyyy-MM-dd</c> form. End is EXCLUSIVE — see class doc.
    /// </param>
    public static ParsedDates? Parse(
        DateTime? timedStartUtc, DateTime? timedEndUtc,
        string? allDayStartString, string? allDayEndString)
    {
        if (timedStartUtc.HasValue && timedEndUtc.HasValue)
        {
            return new ParsedDates(
                DateTime.SpecifyKind(timedStartUtc.Value, DateTimeKind.Utc),
                DateTime.SpecifyKind(timedEndUtc.Value, DateTimeKind.Utc),
                IsAllDay: false);
        }

        if (string.IsNullOrWhiteSpace(allDayStartString) || string.IsNullOrWhiteSpace(allDayEndString))
            return null;

        const DateTimeStyles styles =
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal;
        if (!DateTime.TryParse(allDayStartString, CultureInfo.InvariantCulture, styles, out var allDayStart) ||
            !DateTime.TryParse(allDayEndString, CultureInfo.InvariantCulture, styles, out var allDayEndExclusive))
        {
            return null;
        }

        var startDate = DateTime.SpecifyKind(allDayStart.Date, DateTimeKind.Utc);
        var lastDay = allDayEndExclusive.Date.AddDays(-1);
        if (lastDay < startDate.Date) lastDay = startDate.Date;
        var endDate = DateTime.SpecifyKind(lastDay, DateTimeKind.Utc);
        return new ParsedDates(startDate, endDate, IsAllDay: true);
    }
}
