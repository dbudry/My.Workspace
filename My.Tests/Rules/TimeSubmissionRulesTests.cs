using My.Shared.Rules;
using Xunit;

namespace My.Tests.Rules;

/// <summary>
/// Exercises <see cref="TimeSubmissionRules.IsEarlySubmission"/> — the pure classification
/// used to label a submittable month as "early" (current/future, hasn't ended yet) on the
/// Submit page. This is a labeling helper only; the actual gate on whether a month can be
/// submitted early is "does the user have tracked time in it", enforced in
/// TimeSubmissionFunction.CreateAsync against the database, not here.
/// </summary>
public class TimeSubmissionRulesTests
{
    [Fact]
    public void Past_month_is_not_early()
    {
        var utcNow = new DateTime(2026, 8, 12, 10, 0, 0, DateTimeKind.Utc);

        Assert.False(TimeSubmissionRules.IsEarlySubmission(2026, 7, utcNow));
    }

    [Fact]
    public void Current_month_is_early()
    {
        var utcNow = new DateTime(2026, 8, 12, 10, 0, 0, DateTimeKind.Utc);

        Assert.True(TimeSubmissionRules.IsEarlySubmission(2026, 8, utcNow));
    }

    [Fact]
    public void Future_month_is_early()
    {
        var utcNow = new DateTime(2026, 8, 12, 10, 0, 0, DateTimeKind.Utc);

        Assert.True(TimeSubmissionRules.IsEarlySubmission(2026, 9, utcNow));
    }

    [Fact]
    public void Far_future_month_is_early()
    {
        var utcNow = new DateTime(2026, 8, 12, 10, 0, 0, DateTimeKind.Utc);

        Assert.True(TimeSubmissionRules.IsEarlySubmission(2027, 1, utcNow));
    }

    [Fact]
    public void Last_day_of_current_month_is_still_early_until_month_boundary()
    {
        // "Early" is a calendar-month boundary, not a days-remaining count — the whole
        // current month counts as early even on its last day, since it hasn't rolled
        // over yet from the server's perspective.
        var utcNow = new DateTime(2026, 8, 31, 23, 59, 0, DateTimeKind.Utc);

        Assert.True(TimeSubmissionRules.IsEarlySubmission(2026, 8, utcNow));
    }

    [Fact]
    public void First_moment_of_next_month_makes_previous_month_no_longer_early()
    {
        var utcNow = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);

        Assert.False(TimeSubmissionRules.IsEarlySubmission(2026, 8, utcNow));
    }
}
