namespace My.Shared.Rules;

/// <summary>
/// The payment date is a calendar day. It is stored in <c>ReimbursedAt</c> as the start
/// of that day in the report owner's time zone, so a time of day can be added later.
/// </summary>
public static class ExpensePaymentDateRules
{
    public static DateTime ToStoredUtc(DateTime calendarDate, string? timeZoneId)
    {
        var zone = Zone(timeZoneId);
        var local = DateTime.SpecifyKind(calendarDate.Date, DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(local))
            local = local.AddHours(1);
        return TimeZoneInfo.ConvertTimeToUtc(local, zone);
    }

    public static DateTime ToCalendarDate(DateTime storedUtc, string? timeZoneId)
    {
        var utc = storedUtc.Kind == DateTimeKind.Utc
            ? storedUtc
            : DateTime.SpecifyKind(storedUtc, DateTimeKind.Utc);
        return TimeZoneInfo.ConvertTimeFromUtc(utc, Zone(timeZoneId)).Date;
    }

    public static DateTime? CalendarDay(DateTime? storedUtc, string? timeZoneId = null)
    {
        if (storedUtc == null)
            return null;
        return ToCalendarDate(storedUtc.Value, timeZoneId);
    }

    /// <summary>
    /// Prefer the calendar day the server already resolved in the owner's zone.
    /// Lists that only have the stored instant fall back to that instant.
    /// A blank zone uses <see cref="ExpenseReportRules.DefaultSubmitTimeZoneId"/>.
    /// </summary>
    public static DateTime? ShownDay(bool hasCalendarDate, DateTime calendarDate, DateTime? storedUtc, string? timeZoneId = null)
    {
        if (hasCalendarDate && calendarDate != default)
            return DateTime.SpecifyKind(calendarDate.Date, DateTimeKind.Unspecified);
        return CalendarDay(storedUtc, timeZoneId);
    }

    private static TimeZoneInfo Zone(string? timeZoneId)
    {
        var id = string.IsNullOrWhiteSpace(timeZoneId)
            ? ExpenseReportRules.DefaultSubmitTimeZoneId
            : timeZoneId.Trim();
        return UserTimeZoneRules.Resolve(id);
    }
}
