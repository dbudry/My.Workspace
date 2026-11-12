using My.Shared.Dtos.TimeSubmission;
using My.Shared.Rules;
using Xunit;

namespace My.Tests.Rules;

public class SubmissionGridEmailBuilderTests
{
    private static SubmissionGridDto Grid() => new()
    {
        Months =
        [
            new() { Year = 2026, Month = 10 },
            new() { Year = 2026, Month = 11 }
        ],
        Rows =
        [
            new()
            {
                UserId = "u1", UserName = "Alice <b>Adams</b>",
                Cells =
                [
                    new() { Status = SubmissionCellStatus.Submitted },
                    new() { Status = SubmissionCellStatus.Open }
                ]
            },
            new()
            {
                UserId = "u2", UserName = "Bob Brown",
                Cells =
                [
                    new() { Status = SubmissionCellStatus.Unsubmitted },
                    new() { Status = SubmissionCellStatus.NoData }
                ]
            }
        ]
    };

    [Fact]
    public void Subject_names_the_submitter()
    {
        Assert.Equal("Bob Brown submitted time", SubmissionGridEmailBuilder.Subject("Bob Brown"));
    }

    [Fact]
    public void Html_has_intro_months_and_every_employee()
    {
        var html = SubmissionGridEmailBuilder.Html("Bob Brown", "u2", 2026, 10, Grid(), null);

        Assert.Contains("Bob Brown has put their time in for Oct 2026.", html, StringComparison.Ordinal);
        Assert.Contains("Nov 2026", html, StringComparison.Ordinal);
        Assert.Contains(">Bob Brown</td>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Html_highlights_only_the_submitter_row()
    {
        var html = SubmissionGridEmailBuilder.Html("Bob Brown", "u2", 2026, 10, Grid(), null);

        Assert.Equal(1, CountOccurrences(html, SubmissionGridEmailBuilder.SubmitterRowBackground));
        var bobIndex = html.IndexOf(">Bob Brown</td>", StringComparison.Ordinal);
        var highlightIndex = html.IndexOf(SubmissionGridEmailBuilder.SubmitterRowBackground, StringComparison.Ordinal);
        Assert.True(highlightIndex < bobIndex && bobIndex - highlightIndex < 80);
    }

    [Fact]
    public void Html_colors_unsubmitted_orange_and_submitted_green()
    {
        var html = SubmissionGridEmailBuilder.Html("Bob Brown", "u2", 2026, 10, Grid(), null);

        Assert.Contains(SubmissionGridEmailBuilder.CellColor(SubmissionCellStatus.Unsubmitted), html, StringComparison.Ordinal);
        Assert.Contains(SubmissionGridEmailBuilder.CellColor(SubmissionCellStatus.Submitted), html, StringComparison.Ordinal);
        Assert.Equal("#f0b060", SubmissionGridEmailBuilder.CellColor(SubmissionCellStatus.Unsubmitted));
        Assert.Equal("#5fb374", SubmissionGridEmailBuilder.CellColor(SubmissionCellStatus.Submitted));
    }

    [Fact]
    public void Html_colors_the_cell_background_with_no_pill_labels()
    {
        var html = SubmissionGridEmailBuilder.Html("Bob Brown", "u2", 2026, 10, Grid(), null);

        Assert.DoesNotContain("border-radius", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<span", html, StringComparison.Ordinal);
        Assert.Contains("background:#5fb374", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Html_encodes_names()
    {
        var html = SubmissionGridEmailBuilder.Html("Bob Brown", "u2", 2026, 10, Grid(), null);

        Assert.DoesNotContain("<b>Adams</b>", html, StringComparison.Ordinal);
        Assert.Contains("&lt;b&gt;Adams&lt;/b&gt;", html, StringComparison.Ordinal);
    }

    [Fact]
    public void Html_includes_link_only_when_given()
    {
        Assert.DoesNotContain("<a href", SubmissionGridEmailBuilder.Html("B", "u2", 2026, 10, Grid(), null), StringComparison.Ordinal);
        Assert.Contains("href=\"https://x.test/tyme/submit\"",
            SubmissionGridEmailBuilder.Html("B", "u2", 2026, 10, Grid(), "https://x.test/tyme/submit"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Plain_text_lists_each_employee_with_status()
    {
        var text = SubmissionGridEmailBuilder.PlainText("Bob Brown", 2026, 10, Grid(), null);

        Assert.Contains("Bob Brown: Oct 2026 Not submitted, Nov 2026 No time", text, StringComparison.Ordinal);
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var i = 0;
        while ((i = text.IndexOf(value, i, StringComparison.Ordinal)) >= 0)
        {
            count++;
            i += value.Length;
        }
        return count;
    }
}
