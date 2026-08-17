using My.Shared.Dtos.GoogleCalendar;
using My.Shared.Rules;
using Xunit;

namespace My.Tests.Rules;

public class CalendarPullResultRulesTests
{
    [Fact]
    public void Headline_counts_events_looked_at()
    {
        var r = new CalendarPullResultDto { Scanned = 136 };
        Assert.Equal("Looked at 136 Google events in that date range.", CalendarPullResultRules.Headline(r));
    }

    [Fact]
    public void DetailLines_omit_zeros_and_explain_cancelled_as_kept()
    {
        var r = new CalendarPullResultDto
        {
            Scanned = 136,
            Updated = 15,
            Cancelled = 25,
            SkippedOurs = 7,
            SkippedNoTag = 88,
            SkippedUnresolvedTag = 1
        };

        var lines = CalendarPullResultRules.DetailLines(r);
        Assert.Contains(lines, l => l.Contains("15 existing tasks were updated"));
        Assert.Contains(lines, l => l.Contains("left on your timesheet"));
        Assert.DoesNotContain(lines, l => l.Contains("deleted"));
        Assert.Contains(lines, l => l.Contains("88 had no [project tag]"));
        Assert.Contains(lines, l => l.Contains("[tag] that does not match"));
        Assert.DoesNotContain(lines, l => l.Contains("declined"));
    }

    [Fact]
    public void DetailLines_empty_scan_with_events_says_nothing_changed()
    {
        var r = new CalendarPullResultDto { Scanned = 3 };
        var lines = CalendarPullResultRules.DetailLines(r);
        Assert.Equal(new[] { "Nothing needed to change in Tyme." }, lines);
    }
}
