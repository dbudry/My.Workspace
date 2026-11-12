using My.Shared.Dtos.TimeSubmission;
using My.Shared.Rules;
using Xunit;

namespace My.Tests.Rules;

public class SubmissionGridRulesTests
{
    private static readonly DateTime Now = new(2026, 11, 5, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Default_range_is_last_month_to_next_month()
    {
        var (from, to) = SubmissionGridRules.DefaultRange(Now);

        Assert.Equal((2026, 10), from);
        Assert.Equal((2026, 12), to);
    }

    [Fact]
    public void Default_range_crosses_the_year_boundary()
    {
        var (from, to) = SubmissionGridRules.DefaultRange(new DateTime(2026, 12, 20, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal((2026, 11), from);
        Assert.Equal((2027, 1), to);
    }

    [Fact]
    public void Offsets_are_clamped()
    {
        var (from, to) = SubmissionGridRules.RangeFromOffsets(Now, -4, 99);

        Assert.Equal((2026, 11), from);
        Assert.Equal((2027, 10), to);
    }

    [Fact]
    public void RangeFromOffsets_centers_on_the_submitted_month_for_the_email()
    {
        // The email grid is built around the month being submitted, not today's month.
        var (from, to) = SubmissionGridRules.RangeFromOffsets(new DateTime(2026, 7, 1), 1, 1);

        Assert.Equal((2026, 6), from);
        Assert.Equal((2026, 8), to);
    }

    [Theory]
    [InlineData(2026, 10, 2026, 12, true)]
    [InlineData(2026, 10, 2026, 10, true)]
    [InlineData(2026, 12, 2026, 10, false)]
    [InlineData(2026, 1, 2026, 12, true)]
    [InlineData(2026, 1, 2027, 1, false)]
    [InlineData(2026, 0, 2026, 5, false)]
    [InlineData(1999, 1, 2000, 1, false)]
    public void Range_validation(int fy, int fm, int ty, int tm, bool expected)
    {
        Assert.Equal(expected, SubmissionGridRules.IsValidRange((fy, fm), (ty, tm)));
    }

    [Fact]
    public void Enumerate_months_lists_each_month_in_order()
    {
        var months = SubmissionGridRules.EnumerateMonths((2026, 11), (2027, 2));

        Assert.Equal([(2026, 11), (2026, 12), (2027, 1), (2027, 2)], months);
    }

    [Theory]
    [InlineData("2026-10", 2026, 10)]
    [InlineData(" 2027-01 ", 2027, 1)]
    public void Parse_month_accepts_yyyy_MM(string text, int year, int month)
    {
        Assert.Equal((year, month), SubmissionGridRules.ParseMonth(text));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("2026-13")]
    [InlineData("10/2026")]
    public void Parse_month_rejects_bad_text(string? text)
    {
        Assert.Null(SubmissionGridRules.ParseMonth(text));
    }

    [Theory]
    [InlineData(true, true, 2026, 9, SubmissionCellStatus.Submitted)]
    [InlineData(true, false, 2026, 9, SubmissionCellStatus.Submitted)]
    [InlineData(false, false, 2026, 9, SubmissionCellStatus.NoData)]
    [InlineData(false, true, 2026, 9, SubmissionCellStatus.Unsubmitted)]
    [InlineData(false, true, 2026, 10, SubmissionCellStatus.Unsubmitted)]
    [InlineData(false, true, 2026, 11, SubmissionCellStatus.Open)]
    [InlineData(false, true, 2026, 12, SubmissionCellStatus.Open)]
    [InlineData(false, false, 2026, 12, SubmissionCellStatus.NoData)]
    public void Status_resolution(bool submitted, bool hasTime, int year, int month, SubmissionCellStatus expected)
    {
        Assert.Equal(expected, SubmissionGridRules.ResolveStatus(submitted, hasTime, year, month, Now));
    }

    [Fact]
    public void Parse_ids_is_empty_for_missing_or_broken_json()
    {
        Assert.Empty(SubmissionGridRules.ParseIds(null));
        Assert.Empty(SubmissionGridRules.ParseIds("not json"));
    }

    [Fact]
    public void Parse_ids_trims_and_dedupes()
    {
        Assert.Equal(["a", "b"], SubmissionGridRules.ParseIds("[\" a \",\"b\",\"a\",\"\"]"));
    }

    [Theory]
    [InlineData("3", 3)]
    [InlineData("99", SubmissionGridRules.MaxOffset)]
    [InlineData("-2", 0)]
    [InlineData("x", 1)]
    [InlineData(null, 1)]
    public void Parse_offset(string? text, int expected)
    {
        Assert.Equal(expected, SubmissionGridRules.ParseOffset(text, 1));
    }

    [Theory]
    [InlineData(2026, 10, true)]
    [InlineData(2026, 9, true)]
    [InlineData(2026, 12, true)]
    [InlineData(2026, 8, false)]
    [InlineData(2027, 1, false)]
    public void Is_in_range_is_inclusive_at_both_ends(int year, int month, bool expected)
    {
        Assert.Equal(expected, SubmissionGridRules.IsInRange(year, month, (2026, 9), (2026, 12)));
    }

    [Theory]
    [InlineData(SubmissionCellStatus.Submitted, "submitted")]
    [InlineData(SubmissionCellStatus.Unsubmitted, "unsubmitted")]
    [InlineData(SubmissionCellStatus.Open, "open")]
    [InlineData(SubmissionCellStatus.NoData, "notime")]
    public void Status_key_names_each_cell_status(SubmissionCellStatus status, string key)
    {
        Assert.Equal(key, SubmissionGridRules.StatusKey(status));
    }

    [Fact]
    public void Cell_matches_every_status_when_selection_is_null()
    {
        foreach (var s in Enum.GetValues<SubmissionCellStatus>())
            Assert.True(SubmissionGridRules.CellMatchesStatus(s, null));
    }

    [Fact]
    public void Cell_matches_only_the_picked_statuses()
    {
        string[] picked = ["open", "notime"];

        Assert.True(SubmissionGridRules.CellMatchesStatus(SubmissionCellStatus.Open, picked));
        Assert.True(SubmissionGridRules.CellMatchesStatus(SubmissionCellStatus.NoData, picked));
        Assert.False(SubmissionGridRules.CellMatchesStatus(SubmissionCellStatus.Submitted, picked));
        Assert.False(SubmissionGridRules.CellMatchesStatus(SubmissionCellStatus.Unsubmitted, picked));
    }

    [Fact]
    public void Cell_matches_nothing_when_selection_is_empty()
    {
        foreach (var s in Enum.GetValues<SubmissionCellStatus>())
            Assert.False(SubmissionGridRules.CellMatchesStatus(s, []));
    }

    [Theory]
    [InlineData(null, 4)]
    [InlineData("", 4)]
    [InlineData("all", 4)]
    [InlineData("bogus", 4)]
    [InlineData("open", 1)]
    [InlineData("Open, NoTime", 2)]
    [InlineData("submitted,unsubmitted,open,notime", 4)]
    public void Parse_status_filter_reads_a_list_and_defaults_to_all(string? text, int count)
    {
        Assert.Equal(count, SubmissionGridRules.ParseStatusFilter(text).Count);
    }

    [Fact]
    public void Parse_status_filter_keeps_only_the_named_statuses()
    {
        var set = SubmissionGridRules.ParseStatusFilter("open,notime,junk");

        Assert.Equal(new[] { "notime", "open" }, set.OrderBy(x => x));
    }

    [Fact]
    public void Is_in_range_allows_open_ended_ranges()
    {
        Assert.True(SubmissionGridRules.IsInRange(2020, 1, null, (2026, 12)));
        Assert.False(SubmissionGridRules.IsInRange(2027, 1, null, (2026, 12)));
        Assert.True(SubmissionGridRules.IsInRange(2030, 1, (2026, 9), null));
        Assert.True(SubmissionGridRules.IsInRange(1999, 1, null, null));
    }

    [Fact]
    public void Is_in_range_crosses_a_year_boundary()
    {
        Assert.True(SubmissionGridRules.IsInRange(2026, 1, (2025, 12), (2026, 2)));
        Assert.False(SubmissionGridRules.IsInRange(2025, 11, (2025, 12), (2026, 2)));
    }
}
