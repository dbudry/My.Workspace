using My.Shared.Rules;
using Xunit;

namespace My.Tests.Rules;

public class WeekEntryGridRulesTests
{
    [Theory]
    [InlineData(2026, 5, 11, 2026, 5, 11)] // Monday
    [InlineData(2026, 5, 13, 2026, 5, 11)] // Wednesday → prior Monday
    [InlineData(2026, 5, 17, 2026, 5, 11)] // Sunday → prior Monday
    [InlineData(2026, 5, 10, 2026, 5, 4)]  // Sunday → previous Monday
    public void GetWeekStartMonday_returns_monday(
        int y, int m, int d,
        int ey, int em, int ed)
    {
        var start = WeekEntryGridRules.GetWeekStartMonday(new DateTime(y, m, d));
        Assert.Equal(new DateTime(ey, em, ed), start);
        Assert.Equal(DayOfWeek.Monday, start.DayOfWeek);
    }

    [Fact]
    public void GetWeekDays_returns_seven_mon_through_sun()
    {
        var monday = new DateTime(2026, 5, 11);
        var days = WeekEntryGridRules.GetWeekDays(monday);
        Assert.Equal(7, days.Count);
        Assert.Equal(DayOfWeek.Monday, days[0].DayOfWeek);
        Assert.Equal(DayOfWeek.Sunday, days[6].DayOfWeek);
        Assert.Equal(new DateTime(2026, 5, 17), days[6]);
    }

    [Fact]
    public void GetVisibleWeekDays_business_is_mon_through_fri()
    {
        var monday = new DateTime(2026, 5, 11);
        var days = WeekEntryGridRules.GetVisibleWeekDays(monday, businessWeekOnly: true);
        Assert.Equal(5, days.Count);
        Assert.Equal(DayOfWeek.Monday, days[0].DayOfWeek);
        Assert.Equal(DayOfWeek.Friday, days[4].DayOfWeek);
        Assert.Equal(new DateTime(2026, 5, 15), days[4]);
    }

    [Fact]
    public void GetVisibleWeekDays_full_is_seven()
    {
        var monday = new DateTime(2026, 5, 11);
        var days = WeekEntryGridRules.GetVisibleWeekDays(monday, businessWeekOnly: false);
        Assert.Equal(7, days.Count);
    }

    [Fact]
    public void SumForDay_and_range()
    {
        var tasks = new[]
        {
            Slice("1", "p1", new DateTime(2026, 5, 12), TimeSpan.FromHours(2)),
            Slice("2", "p1", new DateTime(2026, 5, 13), TimeSpan.FromHours(3)),
            Slice("3", "p1", new DateTime(2026, 5, 20), TimeSpan.FromHours(1))
        };
        Assert.Equal(TimeSpan.FromHours(2), WeekEntryGridRules.SumForDay(tasks, new DateTime(2026, 5, 12)));
        Assert.Equal(TimeSpan.FromHours(5),
            WeekEntryGridRules.SumForDateRange(tasks, new DateTime(2026, 5, 12), new DateTime(2026, 5, 13)));
    }

    [Fact]
    public void Weekly_view_total_sums_full_week_including_weekends_and_stopwatch()
    {
        // Tasks Weekly shows Mon–Sun total (not business-week-only).
        var monday = new DateTime(2026, 8, 3);
        var sunday = WeekEntryGridRules.GetWeekEndSunday(monday);
        Assert.Equal(new DateTime(2026, 8, 9), sunday);

        var tasks = new[]
        {
            Slice("1", "p1", monday, TimeSpan.FromHours(2)),
            Slice("2", "p1", monday.AddDays(5), TimeSpan.FromHours(3)), // Saturday
            Slice("3", "p1", monday.AddDays(2), TimeSpan.FromHours(1), stopwatchItemId: "sw"),
            Slice("4", "p1", monday.AddDays(7), TimeSpan.FromHours(9)) // next Monday — outside
        };

        var total = WeekEntryGridRules.SumForDateRange(tasks, monday, sunday);
        Assert.Equal(TimeSpan.FromHours(6), total);
    }

    [Fact]
    public void Week_date_picker_snaps_any_day_to_monday_start()
    {
        // User picks a Thursday → weekly view anchors on that week's Monday.
        var thursday = new DateTime(2026, 8, 6);
        Assert.Equal(new DateTime(2026, 8, 3), WeekEntryGridRules.GetWeekStartMonday(thursday));
    }

    [Fact]
    public void DecideMutation_empty_zero_is_none()
    {
        var m = WeekEntryGridRules.DecideMutation(null, TimeSpan.Zero, TimeSpan.Zero);
        Assert.Equal(WeekEntryGridRules.CellMutationKind.None, m.Kind);
    }

    [Fact]
    public void DecideMutation_positive_without_task_is_create()
    {
        var m = WeekEntryGridRules.DecideMutation(null, TimeSpan.Zero, TimeSpan.FromHours(2));
        Assert.Equal(WeekEntryGridRules.CellMutationKind.Create, m.Kind);
        Assert.Equal(TimeSpan.FromHours(2), m.Duration);
    }

    [Fact]
    public void DecideMutation_zero_with_task_is_delete()
    {
        var m = WeekEntryGridRules.DecideMutation("abc", TimeSpan.FromHours(1), TimeSpan.Zero);
        Assert.Equal(WeekEntryGridRules.CellMutationKind.Delete, m.Kind);
        Assert.Equal("abc", m.TaskId);
    }

    [Fact]
    public void DecideMutation_same_duration_is_none()
    {
        var m = WeekEntryGridRules.DecideMutation(
            "abc", TimeSpan.FromHours(2), TimeSpan.FromHours(2));
        Assert.Equal(WeekEntryGridRules.CellMutationKind.None, m.Kind);
    }

    [Fact]
    public void DecideMutation_changed_duration_is_update()
    {
        var m = WeekEntryGridRules.DecideMutation(
            "abc", TimeSpan.FromHours(2), TimeSpan.FromHours(3.5));
        Assert.Equal(WeekEntryGridRules.CellMutationKind.Update, m.Kind);
        Assert.Equal(TimeSpan.FromHours(3) + TimeSpan.FromMinutes(30), m.Duration);
    }

    [Fact]
    public void DurationFromParts_clamps_minutes_and_hours()
    {
        Assert.Equal(new TimeSpan(2, 30, 0), WeekEntryGridRules.DurationFromParts(2, 30));
        Assert.Equal(new TimeSpan(2, 59, 0), WeekEntryGridRules.DurationFromParts(2, 99));
        Assert.Equal(new TimeSpan(99, 0, 0), WeekEntryGridRules.DurationFromParts(200, 0));
        Assert.Equal(TimeSpan.Zero, WeekEntryGridRules.DurationFromParts(-1, -5));
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("   ", true)]
    [InlineData("a", true)]
    [InlineData("ab", true)]
    [InlineData("Work", true)]
    public void ValidateTaskName_enforces_length(string? name, bool ok)
    {
        var err = WeekEntryGridRules.ValidateTaskDetails(name);
        if (ok)
            Assert.Null(err);
        else
            Assert.NotNull(err);
    }

    [Fact]
    public void ValidateTaskName_rejects_over_max()
    {
        var longName = new string('x', WeekEntryGridRules.MaxTaskNameLength + 1);
        Assert.NotNull(WeekEntryGridRules.ValidateTaskDetails(longName));
    }

    [Theory]
    [InlineData("  Foo  ", "Foo")]
    [InlineData("\tWork\n", "Work")]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("   ", "")]
    [InlineData("No trim needed", "No trim needed")]
    public void SanitizeTaskDetails_trims_leading_and_trailing_whitespace(string? input, string expected)
    {
        // TrackedTaskFunction calls this on create/update before persisting, so leading/
        // trailing whitespace typed by the user (or pasted in) never reaches the database.
        Assert.Equal(expected, WeekEntryGridRules.SanitizeTaskDetails(input));
    }

    [Fact]
    public void SanitizeTaskDetails_preserves_internal_whitespace()
    {
        Assert.Equal("Foo  Bar", WeekEntryGridRules.SanitizeTaskDetails("  Foo  Bar  "));
    }

    [Fact]
    public void SanitizeTaskDetails_preserves_internal_newlines()
    {
        Assert.Equal("Line one\nLine two", WeekEntryGridRules.SanitizeTaskDetails("  Line one\nLine two\n"));
    }

    [Fact]
    public void TruncateTaskName_caps_length()
    {
        var longName = new string('a', WeekEntryGridRules.MaxTaskNameLength + 20);
        var t = WeekEntryGridRules.TruncateTaskName(longName);
        Assert.Equal(WeekEntryGridRules.MaxTaskNameLength, t.Length);
    }

    [Fact]
    public void BindDay_empty_when_no_match()
    {
        var tasks = new[]
        {
            Slice("1", "p1", new DateTime(2026, 5, 12), TimeSpan.FromHours(1))
        };
        var b = WeekEntryGridRules.BindDay(tasks, "p2", new DateTime(2026, 5, 12));
        Assert.Equal(WeekEntryGridRules.DayBindKind.Empty, b.Kind);
    }

    [Fact]
    public void BindDay_single_manual()
    {
        var start = new DateTime(2026, 5, 12, 9, 0, 0);
        var tasks = new[]
        {
            Slice("1", "p1", start, TimeSpan.FromHours(2), name: "Coding")
        };
        var b = WeekEntryGridRules.BindDay(tasks, "p1", new DateTime(2026, 5, 12));
        Assert.Equal(WeekEntryGridRules.DayBindKind.Single, b.Kind);
        Assert.Equal("1", b.TaskId);
        Assert.Equal("Coding", b.TaskName);
        Assert.Equal(TimeSpan.FromHours(2), b.EditableDuration);
    }

    [Fact]
    public void BindDay_multiple_manuals_is_read_only_sum()
    {
        var day = new DateTime(2026, 5, 12, 9, 0, 0);
        var tasks = new[]
        {
            Slice("1", "p1", day, TimeSpan.FromHours(1)),
            Slice("2", "p1", day.AddHours(2), TimeSpan.FromHours(2))
        };
        var b = WeekEntryGridRules.BindDay(tasks, "p1", new DateTime(2026, 5, 12));
        Assert.Equal(WeekEntryGridRules.DayBindKind.Multiple, b.Kind);
        Assert.Null(b.TaskId);
        Assert.Equal(TimeSpan.FromHours(3), b.TotalManualDuration);
    }

    [Fact]
    public void BindDayForTaskDetails_separates_names_on_same_day()
    {
        var day = new DateTime(2026, 5, 12, 9, 0, 0);
        var tasks = new[]
        {
            Slice("1", "p1", day, TimeSpan.FromHours(2), name: "Time Entry"),
            Slice("2", "p1", day.AddHours(3), TimeSpan.FromHours(1), name: "Expenses")
        };

        var timeEntry = WeekEntryGridRules.BindDayForTaskDetails(
            tasks, "p1", "Time Entry", new DateTime(2026, 5, 12));
        Assert.Equal(WeekEntryGridRules.DayBindKind.Single, timeEntry.Kind);
        Assert.Equal("1", timeEntry.TaskId);
        Assert.Equal(TimeSpan.FromHours(2), timeEntry.EditableDuration);

        var expenses = WeekEntryGridRules.BindDayForTaskDetails(
            tasks, "p1", "expenses", new DateTime(2026, 5, 12)); // case-insensitive
        Assert.Equal(WeekEntryGridRules.DayBindKind.Single, expenses.Kind);
        Assert.Equal("2", expenses.TaskId);
        Assert.Equal(TimeSpan.FromHours(1), expenses.EditableDuration);
    }

    [Fact]
    public void DistinctManualTaskDetails_lists_existing_names_in_range()
    {
        var tasks = new[]
        {
            Slice("1", "p1", new DateTime(2026, 5, 12), TimeSpan.FromHours(1), name: "Expenses"),
            Slice("2", "p1", new DateTime(2026, 5, 13), TimeSpan.FromHours(2), name: "Time Entry"),
            Slice("3", "p1", new DateTime(2026, 5, 12), TimeSpan.FromHours(1), name: "time entry"), // same key
            Slice("4", "p2", new DateTime(2026, 5, 12), TimeSpan.FromHours(1), name: "Other"),
            Slice("5", "p1", new DateTime(2026, 5, 20), TimeSpan.FromHours(1), name: "Outside week")
        };

        var names = WeekEntryGridRules.DistinctManualTaskDetails(
            tasks, "p1", new DateTime(2026, 5, 12), new DateTime(2026, 5, 16));

        Assert.Equal(2, names.Count);
        Assert.Contains(names, n => WeekEntryGridRules.TaskNamesEqual(n, "Expenses"));
        Assert.Contains(names, n => WeekEntryGridRules.TaskNamesEqual(n, "Time Entry"));
    }

    [Fact]
    public void DistinctManualTaskDetails_includes_empty_details_row()
    {
        // Details is optional (WeekEntryGridRules.ValidateTaskName) — a task saved
        // with no Details must still surface as a row, not vanish from Project view
        // on the next load.
        var tasks = new[]
        {
            Slice("1", "p1", new DateTime(2026, 5, 12), TimeSpan.FromHours(1), name: "")
        };

        var names = WeekEntryGridRules.DistinctManualTaskDetails(
            tasks, "p1", new DateTime(2026, 5, 12), new DateTime(2026, 5, 16));

        Assert.Single(names);
        Assert.Equal("", names[0]);
    }

    [Fact]
    public void BindDayForTaskDetails_empty_details_binds_hours()
    {
        // Details is optional — blank-name manuals must bind like named ones, or Week → Day
        // shows 0h (and the footer undercounts) while List still has the saved hours.
        var day = new DateTime(2026, 8, 20, 9, 0, 0);
        var tasks = new[]
        {
            Slice("blank", "proj-a", day, TimeSpan.FromHours(2), name: ""),
            Slice("named", "proj-b", day.AddHours(3), TimeSpan.FromMinutes(45), name: "Sample Task")
        };

        var blank = WeekEntryGridRules.BindDayForTaskDetails(
            tasks, "proj-a", "", new DateTime(2026, 8, 20));
        Assert.Equal(WeekEntryGridRules.DayBindKind.Single, blank.Kind);
        Assert.Equal("blank", blank.TaskId);
        Assert.Equal(TimeSpan.FromHours(2), blank.EditableDuration);

        var named = WeekEntryGridRules.BindDayForTaskDetails(
            tasks, "proj-b", "Sample Task", new DateTime(2026, 8, 20));
        Assert.Equal(WeekEntryGridRules.DayBindKind.Single, named.Kind);
        Assert.Equal("named", named.TaskId);
        Assert.Equal(TimeSpan.FromMinutes(45), named.EditableDuration);

        var blankDoesNotStealNamed = WeekEntryGridRules.BindDayForTaskDetails(
            tasks, "proj-b", "", new DateTime(2026, 8, 20));
        Assert.Equal(WeekEntryGridRules.DayBindKind.Empty, blankDoesNotStealNamed.Kind);
    }

    [Fact]
    public void BindDayForTaskDetails_two_empty_details_same_project_day_is_multiple()
    {
        var day = new DateTime(2026, 8, 20, 9, 0, 0);
        var tasks = new[]
        {
            Slice("1", "p1", day, TimeSpan.FromHours(1), name: ""),
            Slice("2", "p1", day.AddHours(2), TimeSpan.FromHours(3), name: "")
        };

        var b = WeekEntryGridRules.BindDayForTaskDetails(
            tasks, "p1", "", new DateTime(2026, 8, 20));
        Assert.Equal(WeekEntryGridRules.DayBindKind.Multiple, b.Kind);
        Assert.Null(b.TaskId);
        Assert.Equal(TimeSpan.FromHours(4), b.TotalManualDuration);
    }

    [Fact]
    public void BindDay_ignores_stopwatch_but_binds_allday_readonly()
    {
        var day = new DateTime(2026, 5, 12, 9, 0, 0);
        var tasks = new[]
        {
            Slice("sw", "p1", day, TimeSpan.FromHours(5), stopwatchItemId: "item1"),
            Slice("ad", "p1", day.Date, TimeSpan.FromHours(8), isAllDay: true)
        };
        var b = WeekEntryGridRules.BindDay(tasks, "p1", new DateTime(2026, 5, 12));
        Assert.Equal(WeekEntryGridRules.DayBindKind.AllDay, b.Kind);
        Assert.Equal(TimeSpan.FromHours(8), b.TotalManualDuration);
    }

    [Fact]
    public void SumDayDuration_all_day_plus_timed_is_nine_hours()
    {
        // List view: all-day IT Support 8h + Administration 1h on Wed → Day footer must be 9h.
        var wed = new DateTime(2026, 7, 1);
        var tasks = new[]
        {
            Slice("ad", "it", wed, TimeSpan.FromHours(8), name: "My.Workspace", isAllDay: true),
            Slice("tm", "admin", wed.AddHours(10), TimeSpan.FromHours(1), name: "Operations Meeting")
        };
        Assert.Equal(TimeSpan.FromHours(9), WeekEntryGridRules.SumDayDuration(tasks, wed));
    }

    [Fact]
    public void SumTotals_project_and_grand()
    {
        var tasks = new[]
        {
            Slice("1", "p1", new DateTime(2026, 5, 12), TimeSpan.FromHours(2)),
            Slice("2", "p2", new DateTime(2026, 5, 13), TimeSpan.FromHours(3)),
            Slice("3", "p1", new DateTime(2026, 5, 14), TimeSpan.FromHours(1), stopwatchItemId: "s")
        };
        var totals = WeekEntryGridRules.SumTotals(tasks, "p1");
        Assert.Equal(TimeSpan.FromHours(3), totals.ProjectTotal);
        Assert.Equal(TimeSpan.FromHours(6), totals.GrandTotal);
    }

    [Fact]
    public void IsDaySubmitted_matches_year_month()
    {
        var submitted = new[] { (2026, 5), (2026, 4) };
        Assert.True(WeekEntryGridRules.IsDaySubmitted(new DateTime(2026, 5, 15), submitted));
        Assert.False(WeekEntryGridRules.IsDaySubmitted(new DateTime(2026, 6, 1), submitted));
    }

    [Theory]
    [InlineData(0, 0, "0h")]
    [InlineData(2, 0, "2h")]
    [InlineData(0, 30, "30m")]
    [InlineData(2, 30, "2h 30m")]
    public void FormatDuration_readable(int h, int m, string expected)
    {
        Assert.Equal(expected, WeekEntryGridRules.FormatDuration(new TimeSpan(h, m, 0)));
    }

    [Theory]
    [InlineData(null, true, 0, 0)]
    [InlineData("", true, 0, 0)]
    [InlineData("2:30", true, 2, 30)]
    [InlineData("0:45", true, 0, 45)]
    [InlineData(":15", true, 0, 15)]
    [InlineData("8", true, 8, 0)]
    [InlineData("2.5", true, 2, 30)]
    [InlineData("2h30m", true, 2, 30)]
    [InlineData("2h", true, 2, 0)]
    [InlineData("90m", true, 1, 30)]
    [InlineData("15m", true, 0, 15)]
    [InlineData("2:99", false, 0, 0)]
    [InlineData("abc", false, 0, 0)]
    public void TryParseDurationText_accepts_common_forms(
        string? raw, bool ok, int hours, int minutes)
    {
        var success = WeekEntryGridRules.TryParseDurationText(raw, out var d);
        Assert.Equal(ok, success);
        if (ok)
            Assert.Equal(new TimeSpan(hours, minutes, 0), d);
    }

    [Theory]
    [InlineData("9:00 AM", true, 9, 0)]
    [InlineData("9:30pm", true, 21, 30)]
    [InlineData("14:30", true, 14, 30)]
    [InlineData("9", true, 9, 0)]
    [InlineData("", false, 0, 0)]
    [InlineData("not-a-time", false, 0, 0)]
    public void TryParseClockTime_accepts_common_forms(
        string raw, bool ok, int hours, int minutes)
    {
        var success = WeekEntryGridRules.TryParseClockTime(raw, out var t);
        Assert.Equal(ok, success);
        if (ok)
            Assert.Equal(new TimeSpan(hours, minutes, 0), t);
    }

    [Fact]
    public void FormatDurationInput_and_round_trip()
    {
        Assert.Equal("", WeekEntryGridRules.FormatDurationInput(TimeSpan.Zero));
        Assert.Equal("2:30", WeekEntryGridRules.FormatDurationInput(TimeSpan.FromHours(2.5)));
        Assert.True(WeekEntryGridRules.TryParseDurationText("2:30", out var d));
        Assert.Equal("2:30", WeekEntryGridRules.FormatDurationInput(d));
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("8:aa", "8")]
    [InlineData("8:00", "08:00")]
    [InlineData(":15", "00:15")]
    [InlineData("15m", "00:15")]
    [InlineData("2h", "02:00")]
    [InlineData("24:00", "23:59")]
    [InlineData("24:01", "23:59")]
    [InlineData("25:00", "23:59")]
    [InlineData("08:99", "08:59")]
    [InlineData("abc", "")]
    [InlineData("7", "7")]
    [InlineData("73", "73")]
    [InlineData("730", "73:0")]
    public void NormalizeDayDurationText_digits_and_clamp(string? raw, string expected)
    {
        Assert.Equal(expected, WeekEntryGridRules.NormalizeDayDurationText(raw));
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("aa:aa", ":")]
    [InlineData("08:00", "08:00")]
    [InlineData("4", "4")]
    [InlineData("4:3", "4:3")]
    [InlineData("4:30", "4:30")]
    [InlineData(":15", ":15")]
    [InlineData("15m", "15m")]
    [InlineData("15M", "15m")]
    [InlineData("2h30m", "2h30m")]
    [InlineData(":", ":")]
    [InlineData(":155", ":15")]
    [InlineData("12345", "1234")] // up to 4 digits so 15m / 1439m can be typed
    [InlineData("12:345", "12:34")]
    public void FilterDurationInputChars_soft_only(string? raw, string expected)
    {
        Assert.Equal(expected, WeekEntryGridRules.FilterDurationInputChars(raw));
    }

    [Theory]
    [InlineData(null, true, 0, 0)]
    [InlineData("", true, 0, 0)]
    [InlineData("8:00", true, 8, 0)]
    [InlineData("08:00", true, 8, 0)]
    [InlineData("00:45", true, 0, 45)]
    [InlineData(":15", true, 0, 15)]
    [InlineData("15m", true, 0, 15)]
    [InlineData("2h", true, 2, 0)]
    [InlineData("2h30m", true, 2, 30)]
    [InlineData("90m", true, 1, 30)]
    [InlineData(":1", false, 0, 0)]
    [InlineData(":", false, 0, 0)]
    [InlineData("2h30", false, 0, 0)]
    [InlineData("23:59", true, 23, 59)]
    [InlineData("24:00", false, 0, 0)]
    [InlineData("24:01", false, 0, 0)]
    [InlineData("25:00", false, 0, 0)]
    [InlineData("08:99", false, 0, 0)]
    [InlineData("8", false, 0, 0)]
    [InlineData("08:3", false, 0, 0)]
    [InlineData("8:aa", false, 0, 0)]
    [InlineData("2.5", false, 0, 0)]
    public void TryParseDayDurationText_hhmm_max_storage(
        string? raw, bool ok, int hours, int minutes)
    {
        var success = WeekEntryGridRules.TryParseDayDurationText(raw, out var d);
        Assert.Equal(ok, success);
        if (ok)
            Assert.Equal(new TimeSpan(hours, minutes, 0), d);
    }

    [Fact]
    public void FormatDayDurationInput_pads_and_caps()
    {
        Assert.Equal("", WeekEntryGridRules.FormatDayDurationInput(TimeSpan.Zero));
        Assert.Equal("02:30", WeekEntryGridRules.FormatDayDurationInput(TimeSpan.FromHours(2.5)));
        Assert.Equal("23:59", WeekEntryGridRules.FormatDayDurationInput(TimeSpan.FromHours(30)));
        Assert.True(WeekEntryGridRules.TryParseDayDurationText("08:00", out var d));
        Assert.Equal("08:00", WeekEntryGridRules.FormatDayDurationInput(d));
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("   ", true)]
    [InlineData("sample", true)]
    [InlineData("SAMPLE", true)]
    [InlineData("xyz", false)]
    public void MatchesEntrySearch_is_case_insensitive_substring(string? query, bool match)
    {
        Assert.Equal(match, WeekEntryGridRules.MatchesEntrySearch(query, "Sample Project Support", "Standup"));
    }

    [Fact]
    public void CanEnterDraftDayDuration_requires_project_only()
    {
        Assert.False(WeekEntryGridRules.CanEnterDraftDayDuration(hasProject: false));
        Assert.True(WeekEntryGridRules.CanEnterDraftDayDuration(hasProject: true));
    }

    [Theory]
    [InlineData("", true)]
    [InlineData("00:30", true)]
    [InlineData(":15", true)]
    [InlineData("15m", true)]
    [InlineData("2h", true)]
    [InlineData("8:00", true)]
    [InlineData("00:00", true)]
    [InlineData(":", false)]
    [InlineData(":1", false)]
    [InlineData("0", false)]
    [InlineData("00", false)]
    [InlineData("00:", false)]
    [InlineData("00:3", false)]
    [InlineData("4", false)]
    [InlineData("4:", false)]
    [InlineData("4:3", false)]
    public void ShouldAutosaveDayDurationText_waits_for_complete_minutes(string? raw, bool autosave)
    {
        Assert.Equal(autosave, WeekEntryGridRules.ShouldAutosaveDayDurationText(raw));
    }

    [Fact]
    public void TryCommitDayDurationText_accepts_zero_hours_thirty_minutes()
    {
        Assert.True(WeekEntryGridRules.TryCommitDayDurationText("00:30", out var d));
        Assert.Equal(TimeSpan.FromMinutes(30), d);
    }

    [Theory]
    [InlineData("4", true, 4, 0)]
    [InlineData("04", true, 4, 0)]
    [InlineData("4:", true, 4, 0)]
    [InlineData("4:3", true, 4, 3)]
    [InlineData("04:30", true, 4, 30)]
    [InlineData(":15", true, 0, 15)]
    [InlineData(":5", true, 0, 5)]
    [InlineData("15m", true, 0, 15)]
    [InlineData("90m", true, 1, 30)]
    [InlineData("2h30", true, 2, 30)]
    [InlineData(":", false, 0, 0)]
    [InlineData("23:59", true, 23, 59)]
    [InlineData("24:00", false, 0, 0)]
    [InlineData("25", false, 0, 0)]
    [InlineData("", true, 0, 0)]
    public void TryCommitDayDurationText_accepts_bare_hours(
        string? raw, bool ok, int hours, int minutes)
    {
        var success = WeekEntryGridRules.TryCommitDayDurationText(raw, out var d);
        Assert.Equal(ok, success);
        if (ok)
            Assert.Equal(new TimeSpan(hours, minutes, 0), d);
    }

    private static WeekEntryGridRules.WeekEntryTaskSlice Slice(
        string id,
        string? projectId,
        DateTime start,
        TimeSpan duration,
        string name = "Task",
        bool isAllDay = false,
        string? stopwatchItemId = null,
        DateTime? end = null) =>
        new(id, name, projectId, start, duration, isAllDay, stopwatchItemId, end);
}
