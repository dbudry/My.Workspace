using My.Shared.Dtos.TimeSubmission;
using My.Shared.Rules;
using My.Shared.Validation;
using Xunit;

namespace My.Tests.Rules;

public class UnsubmittedReminderTests
{
    private static SubmissionGridDto OneRowGrid(params (int Year, int Month)[] months) => new()
    {
        Months = months.Select(m => new SubmissionGridMonthDto { Year = m.Year, Month = m.Month }).ToList(),
        Rows =
        [
            new()
            {
                UserId = "u1", UserName = "Alice <b>Adams</b>",
                Cells = months.Select(_ => new SubmissionGridCellDto { Status = SubmissionCellStatus.Unsubmitted }).ToList()
            }
        ]
    };

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("false", false)]
    [InlineData("nope", false)]
    [InlineData("true", true)]
    [InlineData("True", true)]
    public void Setting_is_off_unless_true(string? value, bool expected)
    {
        Assert.Equal(expected, UnsubmittedReminderRules.IsEnabled(value));
    }

    [Fact]
    public void Window_by_month_clicked_is_just_that_month()
    {
        var (from, to) = UnsubmittedReminderRules.Window("month", 2026, 8, 3, 3);

        Assert.Equal((2026, 8), from);
        Assert.Equal((2026, 8), to);
    }

    [Fact]
    public void Window_by_range_counts_months_before_and_after_the_clicked_month()
    {
        var (from, to) = UnsubmittedReminderRules.Window("range", 2026, 1, 2, 1);

        Assert.Equal((2025, 11), from);
        Assert.Equal((2026, 2), to);
    }

    [Fact]
    public void Window_by_year_is_the_calendar_year_of_the_clicked_month()
    {
        var (from, to) = UnsubmittedReminderRules.Window("year", 2026, 8, 0, 0);

        Assert.Equal((2026, 1), from);
        Assert.Equal((2026, 12), to);
    }

    [Fact]
    public void Window_falls_back_to_month_clicked_for_an_unknown_coverage()
    {
        var (from, to) = UnsubmittedReminderRules.Window("nonsense", 2026, 8, 3, 3);

        Assert.Equal(((2026, 8), (2026, 8)), (from, to));
    }

    [Fact]
    public void Month_clicked_allows_only_unsubmitted_and_clears_the_rest()
    {
        var statuses = UnsubmittedReminderRules.NormalizeStatuses("month", ["submitted", "open", "notime", "unsubmitted"]);

        Assert.Equal(["unsubmitted"], statuses);
    }

    [Fact]
    public void Range_and_year_always_keep_unsubmitted_and_drop_unknown_keys()
    {
        Assert.Equal(["unsubmitted"], UnsubmittedReminderRules.NormalizeStatuses("range", []));
        Assert.Equal(["unsubmitted", "open"], UnsubmittedReminderRules.NormalizeStatuses("year", ["open", "bogus"]));
        Assert.Equal(["submitted", "unsubmitted", "open", "notime"],
            UnsubmittedReminderRules.NormalizeStatuses("range", ["notime", "open", "unsubmitted", "submitted"]));
    }

    [Fact]
    public void Statuses_round_trip_through_the_stored_text()
    {
        var text = UnsubmittedReminderRules.StatusesToText(["unsubmitted", "open"]);

        Assert.Equal("unsubmitted,open", text);
        Assert.Equal(["unsubmitted", "open"], UnsubmittedReminderRules.ParseStatuses(text));
        Assert.Empty(UnsubmittedReminderRules.ParseStatuses(null));
    }

    [Fact]
    public void Email_names_only_the_unsubmitted_months_but_shows_every_grid_column()
    {
        var grid = new SubmissionGridDto
        {
            Months = [new() { Year = 2026, Month = 7 }, new() { Year = 2026, Month = 8 }, new() { Year = 2026, Month = 9 }],
            Rows =
            [
                new()
                {
                    UserId = "u1", UserName = "Alice",
                    Cells =
                    [
                        new() { Status = SubmissionCellStatus.Unsubmitted },
                        new() { Status = SubmissionCellStatus.Submitted },
                        new() { Status = SubmissionCellStatus.Open }
                    ]
                }
            ]
        };

        var html = UnsubmittedReminderEmailBuilder.Html("Alice", "Mia", grid, "https://x.test", null);

        Assert.Contains("for the month Jul 2026.", html, StringComparison.Ordinal);
        Assert.Contains(">Sep 2026</th>", html, StringComparison.Ordinal);
        Assert.Contains(">In progress</td>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Settings_validator_rejects_bad_coverage_status_and_oversized_range()
    {
        var validator = new TymeReminderSettingsDtoValidator();

        Assert.True(validator.Validate(new TymeReminderSettingsDto()).IsValid);
        Assert.False(validator.Validate(new TymeReminderSettingsDto { Coverage = "week" }).IsValid);
        Assert.False(validator.Validate(new TymeReminderSettingsDto { Statuses = ["bogus"] }).IsValid);
        Assert.False(validator.Validate(new TymeReminderSettingsDto { Coverage = "range", MonthsBefore = 6, MonthsAfter = 6 }).IsValid);
    }

    [Fact]
    public void Month_list_reads_naturally()
    {
        static List<SubmissionGridMonthDto> M(params int[] months) =>
            months.Select(m => new SubmissionGridMonthDto { Year = 2026, Month = m }).ToList();

        Assert.Equal("", UnsubmittedReminderRules.MonthList(M()));
        Assert.Equal("Aug 2026", UnsubmittedReminderRules.MonthList(M(8)));
        Assert.Equal("Aug 2026 and Sep 2026", UnsubmittedReminderRules.MonthList(M(8, 9)));
        Assert.Equal("Aug 2026, Sep 2026 and Oct 2026", UnsubmittedReminderRules.MonthList(M(8, 9, 10)));
    }

    [Fact]
    public void Email_uses_the_requested_wording_link_and_signature()
    {
        var grid = OneRowGrid((2026, 8), (2026, 9));

        var html = UnsubmittedReminderEmailBuilder.Html(
            "Alice Adams", "Mia Manager", grid, "https://app.example.com/tyme/submit", null);

        Assert.Contains("Alice Adams,", html, StringComparison.Ordinal);
        Assert.Contains("You may have forgotten to submit your time using <a href=\"https://app.example.com/tyme/submit\">Tyme</a> for the months Aug 2026 and Sep 2026. At your next availability, can you take a moment to address this?",
            html, StringComparison.Ordinal);
        Assert.Contains("Thanks,<br>Mia Manager", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<img", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Email_says_month_for_one_and_months_for_many()
    {
        Assert.Contains("for the month Aug 2026.",
            UnsubmittedReminderEmailBuilder.Html("A", "M", OneRowGrid((2026, 8)), "https://x.test", null), StringComparison.Ordinal);
        Assert.Contains("for the months Aug 2026 and Sep 2026.",
            UnsubmittedReminderEmailBuilder.Html("A", "M", OneRowGrid((2026, 8), (2026, 9)), "https://x.test", null), StringComparison.Ordinal);
    }

    [Fact]
    public void Email_grid_uses_the_same_orange_cells_as_the_Grid_and_no_highlight()
    {
        var html = UnsubmittedReminderEmailBuilder.Html("A", "M", OneRowGrid((2026, 8)), "https://x.test", null);

        Assert.Contains("background:" + SubmissionGridEmailBuilder.CellColor(SubmissionCellStatus.Unsubmitted), html, StringComparison.Ordinal);
        Assert.DoesNotContain(SubmissionGridEmailBuilder.SubmitterRowBackground, html, StringComparison.Ordinal);
        Assert.DoesNotContain("<span", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Email_encodes_names_and_omits_logo_when_none()
    {
        var html = UnsubmittedReminderEmailBuilder.Html("<i>Al</i>", "<b>M</b>", OneRowGrid((2026, 8)), "https://x.test", null);

        Assert.DoesNotContain("<i>Al</i>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<b>M</b>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<img", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Plain_text_has_greeting_link_and_signature()
    {
        var text = UnsubmittedReminderEmailBuilder.PlainText(
            "Alice Adams", "Mia Manager", OneRowGrid((2026, 8)), "https://app.example.com/tyme/submit");

        Assert.Contains("Alice Adams,", text, StringComparison.Ordinal);
        Assert.Contains("for the month Aug 2026", text, StringComparison.Ordinal);
        Assert.Contains("https://app.example.com/tyme/submit", text, StringComparison.Ordinal);
        Assert.Contains("Thanks,", text, StringComparison.Ordinal);
        Assert.Contains("Mia Manager", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Request_validator_needs_an_employee()
    {
        var validator = new SendUnsubmittedReminderDtoValidator();

        Assert.False(validator.Validate(new SendUnsubmittedReminderDto { UserId = "", Year = 2026, Month = 8 }).IsValid);
        Assert.False(validator.Validate(new SendUnsubmittedReminderDto { UserId = "u1", Year = 2026, Month = 13 }).IsValid);
        Assert.True(validator.Validate(new SendUnsubmittedReminderDto { UserId = "u1", Year = 2026, Month = 8 }).IsValid);
    }
}
