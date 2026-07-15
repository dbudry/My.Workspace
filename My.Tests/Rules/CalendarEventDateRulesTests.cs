using My.Shared.Rules;
using Xunit;

namespace My.Tests.Rules;

/// <summary>
/// Pins the inbound-import date parsing. The cardinal bugs this guards against:
///
/// - All-day events silently dropped because the original logic only handled timed
///   events ("New Event For derek [ooo]" spanning a week never made it into Tyme).
/// - Multi-day all-day events losing days because Google's <c>end</c> is exclusive —
///   forgetting the <c>-1 day</c> dance lops a day off.
/// - Date strings parsed without <c>AssumeUniversal</c> shifting by the function-app
///   server's local zone.
/// </summary>
public class CalendarEventDateRulesTests
{
    // ---------- Timed events ----------

    [Fact]
    public void Timed_event_preserves_utc_and_flags_not_all_day()
    {
        var start = new DateTime(2026, 5, 21, 14, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2026, 5, 21, 15, 0, 0, DateTimeKind.Utc);

        var parsed = CalendarEventDateRules.Parse(start, end, null, null);

        Assert.NotNull(parsed);
        Assert.False(parsed!.IsAllDay);
        Assert.Equal(start, parsed.StartDate);
        Assert.Equal(end, parsed.EndDate);
        Assert.Equal(DateTimeKind.Utc, parsed.StartDate.Kind);
        Assert.Equal(DateTimeKind.Utc, parsed.EndDate.Kind);
    }

    [Fact]
    public void Timed_event_with_unspecified_kind_is_stamped_utc()
    {
        // Google's library hands us DateTimeDateTimeOffset.UtcDateTime which already
        // returns Kind=Utc — but the helper stamps it defensively so a caller that
        // accidentally hands us Kind=Unspecified gets the right convention.
        var start = new DateTime(2026, 5, 21, 14, 0, 0, DateTimeKind.Unspecified);
        var end = new DateTime(2026, 5, 21, 15, 0, 0, DateTimeKind.Unspecified);

        var parsed = CalendarEventDateRules.Parse(start, end, null, null);

        Assert.NotNull(parsed);
        Assert.Equal(DateTimeKind.Utc, parsed!.StartDate.Kind);
        Assert.Equal(DateTimeKind.Utc, parsed.EndDate.Kind);
    }

    // ---------- All-day single-day ----------

    [Fact]
    public void All_day_single_day_parses_to_same_start_and_end_date()
    {
        // Google sends single-day all-day as start=2026-05-21, end=2026-05-22 (exclusive).
        // Tyme stores start=end=2026-05-21 (inclusive last day).
        var parsed = CalendarEventDateRules.Parse(null, null, "2026-05-21", "2026-05-22");

        Assert.NotNull(parsed);
        Assert.True(parsed!.IsAllDay);
        Assert.Equal(new DateTime(2026, 5, 21, 0, 0, 0, DateTimeKind.Utc), parsed.StartDate);
        Assert.Equal(new DateTime(2026, 5, 21, 0, 0, 0, DateTimeKind.Utc), parsed.EndDate);
    }

    // ---------- All-day multi-day (the "New Event For derek [ooo] spanning a week" case) ----------

    [Fact]
    public void All_day_five_day_event_rolls_end_back_to_inclusive_last_day()
    {
        // Mon May 18 through Fri May 22 arrives as start=Mon, end=Sat.
        var parsed = CalendarEventDateRules.Parse(null, null, "2026-05-18", "2026-05-23");

        Assert.NotNull(parsed);
        Assert.True(parsed!.IsAllDay);
        Assert.Equal(new DateTime(2026, 5, 18, 0, 0, 0, DateTimeKind.Utc), parsed.StartDate);
        Assert.Equal(new DateTime(2026, 5, 22, 0, 0, 0, DateTimeKind.Utc), parsed.EndDate);
    }

    [Fact]
    public void All_day_event_spanning_weekend_keeps_calendar_span()
    {
        // Fri May 22 through Mon May 25 (long-weekend trip). End-exclusive = Tue May 26.
        // The duration helper handles workday math; this parser keeps the calendar span.
        var parsed = CalendarEventDateRules.Parse(null, null, "2026-05-22", "2026-05-26");

        Assert.NotNull(parsed);
        Assert.True(parsed!.IsAllDay);
        Assert.Equal(new DateTime(2026, 5, 22, 0, 0, 0, DateTimeKind.Utc), parsed.StartDate);
        Assert.Equal(new DateTime(2026, 5, 25, 0, 0, 0, DateTimeKind.Utc), parsed.EndDate);
    }

    [Fact]
    public void All_day_multi_month_event_handled_correctly()
    {
        // Boundary case: start=Apr 30, end=May 4 (3 inclusive days across month boundary).
        var parsed = CalendarEventDateRules.Parse(null, null, "2026-04-30", "2026-05-04");

        Assert.NotNull(parsed);
        Assert.Equal(new DateTime(2026, 4, 30, 0, 0, 0, DateTimeKind.Utc), parsed!.StartDate);
        Assert.Equal(new DateTime(2026, 5, 3, 0, 0, 0, DateTimeKind.Utc), parsed.EndDate);
    }

    // ---------- All-day pathological inputs ----------

    [Fact]
    public void All_day_with_end_before_start_collapses_to_single_day()
    {
        // Defensive: Google shouldn't ever send this but we should not crash.
        var parsed = CalendarEventDateRules.Parse(null, null, "2026-05-21", "2026-05-21");

        Assert.NotNull(parsed);
        Assert.True(parsed!.IsAllDay);
        // end-exclusive becomes start - 1 day, clamped back up to start.
        Assert.Equal(parsed.StartDate, parsed.EndDate);
    }

    [Fact]
    public void All_day_with_inverted_range_collapses_to_start_day()
    {
        var parsed = CalendarEventDateRules.Parse(null, null, "2026-05-22", "2026-05-18");

        Assert.NotNull(parsed);
        Assert.True(parsed!.IsAllDay);
        Assert.Equal(new DateTime(2026, 5, 22, 0, 0, 0, DateTimeKind.Utc), parsed.StartDate);
        Assert.Equal(new DateTime(2026, 5, 22, 0, 0, 0, DateTimeKind.Utc), parsed.EndDate);
    }

    [Fact]
    public void All_day_date_kind_is_always_utc_regardless_of_machine_tz()
    {
        // Without AssumeUniversal the parser would treat "2026-05-21" as local time on
        // the function-app server, shifting the date by the offset on non-UTC hosts.
        var parsed = CalendarEventDateRules.Parse(null, null, "2026-05-21", "2026-05-22");

        Assert.NotNull(parsed);
        Assert.Equal(DateTimeKind.Utc, parsed!.StartDate.Kind);
        Assert.Equal(DateTimeKind.Utc, parsed.EndDate.Kind);
        Assert.Equal(21, parsed.StartDate.Day);
    }

    // ---------- Missing / malformed inputs ----------

    [Fact]
    public void Both_shapes_missing_returns_null()
    {
        var parsed = CalendarEventDateRules.Parse(null, null, null, null);
        Assert.Null(parsed);
    }

    [Fact]
    public void Only_start_present_returns_null()
    {
        Assert.Null(CalendarEventDateRules.Parse(new DateTime(2026, 5, 21, 14, 0, 0, DateTimeKind.Utc), null, null, null));
        Assert.Null(CalendarEventDateRules.Parse(null, null, "2026-05-21", null));
        Assert.Null(CalendarEventDateRules.Parse(null, null, null, "2026-05-22"));
    }

    [Fact]
    public void Empty_all_day_strings_return_null()
    {
        Assert.Null(CalendarEventDateRules.Parse(null, null, "", "2026-05-22"));
        Assert.Null(CalendarEventDateRules.Parse(null, null, "2026-05-21", ""));
        Assert.Null(CalendarEventDateRules.Parse(null, null, "   ", "   "));
    }

    [Fact]
    public void Malformed_all_day_string_returns_null()
    {
        Assert.Null(CalendarEventDateRules.Parse(null, null, "not-a-date", "2026-05-22"));
        Assert.Null(CalendarEventDateRules.Parse(null, null, "2026-05-21", "garbage"));
    }

    // ---------- Timed event preferred over all-day when both shapes present ----------
    //
    // Google shouldn't send both, but if it does the timed values are the authoritative
    // ones — they carry timezone offset information that the all-day strings throw away.

    [Fact]
    public void Timed_takes_precedence_when_both_shapes_present()
    {
        var start = new DateTime(2026, 5, 21, 14, 0, 0, DateTimeKind.Utc);
        var end = new DateTime(2026, 5, 21, 15, 0, 0, DateTimeKind.Utc);

        var parsed = CalendarEventDateRules.Parse(start, end, "2026-05-21", "2026-05-22");

        Assert.NotNull(parsed);
        Assert.False(parsed!.IsAllDay);
        Assert.Equal(start, parsed.StartDate);
        Assert.Equal(end, parsed.EndDate);
    }
}
