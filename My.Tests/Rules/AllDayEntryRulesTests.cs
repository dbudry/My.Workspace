using My.Shared.Rules;
using Xunit;

namespace My.Tests.Rules;

/// <summary>
/// Pins the all-day TrackedTask math: workday-hours parsing, inclusive day span,
/// derived Duration, and the Google date-pair shape (end-exclusive).
/// </summary>
public class AllDayEntryRulesTests
{
    // ---------- ParseWorkdayHours: happy path ----------

    [Theory]
    [InlineData("8", 8.0)]
    [InlineData("8.0", 8.0)]
    [InlineData("7.5", 7.5)]
    [InlineData("10", 10.0)]
    [InlineData("0.5", 0.5)]
    [InlineData("24", 24.0)]
    public void ParseWorkdayHours_returns_parsed_value_within_bounds(string raw, double expected)
    {
        Assert.Equal(expected, AllDayEntryRules.ParseWorkdayHours(raw));
    }

    // ---------- ParseWorkdayHours: bad / missing input falls back ----------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    [InlineData("eight")]
    public void Missing_or_unparseable_falls_back_to_default(string? raw)
    {
        Assert.Equal(AllDayEntryRules.DefaultWorkdayHours, AllDayEntryRules.ParseWorkdayHours(raw));
    }

    // ---------- ParseWorkdayHours: pathological values clamped ----------

    [Theory]
    [InlineData("-5", AllDayEntryRules.MinWorkdayHours)]
    [InlineData("0", AllDayEntryRules.MinWorkdayHours)]
    [InlineData("0.1", AllDayEntryRules.MinWorkdayHours)]
    [InlineData("99", AllDayEntryRules.MaxWorkdayHours)]
    [InlineData("25", AllDayEntryRules.MaxWorkdayHours)]
    public void Out_of_range_values_are_clamped(string raw, double expected)
    {
        Assert.Equal(expected, AllDayEntryRules.ParseWorkdayHours(raw));
    }

    [Fact]
    public void NaN_falls_back_to_default()
    {
        // ParseWorkdayHours sees the literal "NaN" string; double.TryParse handles it.
        Assert.Equal(AllDayEntryRules.DefaultWorkdayHours, AllDayEntryRules.ParseWorkdayHours("NaN"));
    }

    [Fact]
    public void Parse_is_culture_invariant()
    {
        // "8,5" in de-DE means 8.5 — but the AppSetting value is the canonical
        // invariant representation. Make sure we don't accidentally trust the
        // ambient culture and read "8,5" as 85.
        var prev = System.Threading.Thread.CurrentThread.CurrentCulture;
        try
        {
            System.Threading.Thread.CurrentThread.CurrentCulture =
                new System.Globalization.CultureInfo("de-DE");
            Assert.Equal(8.5, AllDayEntryRules.ParseWorkdayHours("8.5"));
        }
        finally
        {
            System.Threading.Thread.CurrentThread.CurrentCulture = prev;
        }
    }

    // ---------- InclusiveDaySpan ----------

    [Fact]
    public void Single_day_with_null_end_is_one_day()
    {
        Assert.Equal(1, AllDayEntryRules.InclusiveDaySpan(new DateTime(2026, 5, 19), null));
    }

    [Fact]
    public void Same_day_start_and_end_is_one_day()
    {
        Assert.Equal(1, AllDayEntryRules.InclusiveDaySpan(
            new DateTime(2026, 5, 19, 0, 0, 0),
            new DateTime(2026, 5, 19, 23, 59, 0)));
    }

    [Fact]
    public void Mon_to_fri_inclusive_is_five_days()
    {
        Assert.Equal(5, AllDayEntryRules.InclusiveDaySpan(
            new DateTime(2026, 5, 18), // Monday
            new DateTime(2026, 5, 22))); // Friday
    }

    [Fact]
    public void Span_ignores_time_of_day()
    {
        // Start at 11pm, end at 1am next day — still 2 inclusive calendar days.
        Assert.Equal(2, AllDayEntryRules.InclusiveDaySpan(
            new DateTime(2026, 5, 19, 23, 0, 0),
            new DateTime(2026, 5, 20, 1, 0, 0)));
    }

    [Fact]
    public void Inverted_range_falls_back_to_one_day()
    {
        Assert.Equal(1, AllDayEntryRules.InclusiveDaySpan(
            new DateTime(2026, 5, 22),
            new DateTime(2026, 5, 18)));
    }

    // ---------- WorkdaysInSpan ----------

    [Fact]
    public void Single_weekday_with_null_end_is_one_workday()
    {
        // 2026-05-19 is a Tuesday.
        Assert.Equal(1, AllDayEntryRules.WorkdaysInSpan(new DateTime(2026, 5, 19), null));
    }

    [Fact]
    public void Single_saturday_is_zero_workdays()
    {
        // 2026-05-23 is a Saturday.
        Assert.Equal(0, AllDayEntryRules.WorkdaysInSpan(new DateTime(2026, 5, 23), null));
    }

    [Fact]
    public void Single_sunday_is_zero_workdays()
    {
        // 2026-05-24 is a Sunday.
        Assert.Equal(0, AllDayEntryRules.WorkdaysInSpan(new DateTime(2026, 5, 24), null));
    }

    [Fact]
    public void Mon_to_fri_is_five_workdays()
    {
        Assert.Equal(5, AllDayEntryRules.WorkdaysInSpan(
            new DateTime(2026, 5, 18), // Monday
            new DateTime(2026, 5, 22))); // Friday
    }

    [Fact]
    public void Fri_to_next_mon_is_two_workdays()
    {
        // The cardinal weekend case: a long-weekend vacation Fri→Mon should be 2
        // workdays (Fri + Mon), not 4 calendar days.
        Assert.Equal(2, AllDayEntryRules.WorkdaysInSpan(
            new DateTime(2026, 5, 22), // Friday
            new DateTime(2026, 5, 25))); // Monday
    }

    [Fact]
    public void Mon_to_next_mon_is_six_workdays()
    {
        // Calendar span = 8 days; workdays = Mon, Tue, Wed, Thu, Fri, Mon = 6.
        Assert.Equal(6, AllDayEntryRules.WorkdaysInSpan(
            new DateTime(2026, 5, 18), // Monday
            new DateTime(2026, 5, 25))); // next Monday
    }

    [Fact]
    public void Weekend_only_span_is_zero_workdays()
    {
        Assert.Equal(0, AllDayEntryRules.WorkdaysInSpan(
            new DateTime(2026, 5, 23), // Saturday
            new DateTime(2026, 5, 24))); // Sunday
    }

    [Fact]
    public void Three_week_span_is_fifteen_workdays()
    {
        // Mon May 4 through Fri May 22 — three full work weeks.
        Assert.Equal(15, AllDayEntryRules.WorkdaysInSpan(
            new DateTime(2026, 5, 4),
            new DateTime(2026, 5, 22)));
    }

    [Fact]
    public void Inverted_workday_range_falls_back_to_start_day_only()
    {
        // Fri counted, Mon end ignored because Mon is before Fri (inverted).
        Assert.Equal(1, AllDayEntryRules.WorkdaysInSpan(
            new DateTime(2026, 5, 22), // Friday
            new DateTime(2026, 5, 18))); // earlier Monday (inverted)
    }

    // ---------- DurationFor (workday-based) ----------

    [Fact]
    public void DurationFor_single_weekday_is_workday_hours()
    {
        var d = AllDayEntryRules.DurationFor(new DateTime(2026, 5, 19), null, 8.0);
        Assert.Equal(TimeSpan.FromHours(8), d);
    }

    [Fact]
    public void DurationFor_single_weekend_day_is_zero()
    {
        var d = AllDayEntryRules.DurationFor(new DateTime(2026, 5, 23), null, 8.0);
        Assert.Equal(TimeSpan.Zero, d);
    }

    [Fact]
    public void DurationFor_week_long_vacation_is_five_workdays()
    {
        var d = AllDayEntryRules.DurationFor(
            new DateTime(2026, 5, 18),
            new DateTime(2026, 5, 22),
            7.5);
        Assert.Equal(TimeSpan.FromHours(37.5), d);
    }

    [Fact]
    public void DurationFor_friday_to_next_monday_logs_two_workdays_not_four()
    {
        // Fri + Sat + Sun + Mon → 2 workdays × 8h = 16h.
        var d = AllDayEntryRules.DurationFor(
            new DateTime(2026, 5, 22),
            new DateTime(2026, 5, 25),
            8.0);
        Assert.Equal(TimeSpan.FromHours(16), d);
    }

    // ---------- FormatAllDayForGoogle: end is exclusive ----------
    //
    // Google's API quirk: all-day events use start.date / end.date with end exclusive.
    // A Mon→Fri vacation must be start=Mon, end=Saturday — or Google renders Mon→Thu.

    [Fact]
    public void Single_day_emits_end_one_day_later()
    {
        var (start, end) = AllDayEntryRules.FormatAllDayForGoogle(new DateTime(2026, 5, 19), null);
        Assert.Equal("2026-05-19", start);
        Assert.Equal("2026-05-20", end);
    }

    [Fact]
    public void Multi_day_emits_end_day_after_last_day()
    {
        var (start, end) = AllDayEntryRules.FormatAllDayForGoogle(
            new DateTime(2026, 5, 18),
            new DateTime(2026, 5, 22));
        Assert.Equal("2026-05-18", start);
        Assert.Equal("2026-05-23", end);
    }

    [Fact]
    public void Time_components_are_stripped()
    {
        var (start, end) = AllDayEntryRules.FormatAllDayForGoogle(
            new DateTime(2026, 5, 18, 23, 30, 0),
            new DateTime(2026, 5, 22, 1, 15, 0));
        Assert.Equal("2026-05-18", start);
        Assert.Equal("2026-05-23", end);
    }

    [Fact]
    public void Inverted_range_collapses_to_single_day()
    {
        var (start, end) = AllDayEntryRules.FormatAllDayForGoogle(
            new DateTime(2026, 5, 22),
            new DateTime(2026, 5, 18));
        Assert.Equal("2026-05-22", start);
        Assert.Equal("2026-05-23", end);
    }
}
