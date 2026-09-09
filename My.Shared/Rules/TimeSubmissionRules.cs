namespace My.Shared.Rules;

/// <summary>
/// Pure classification helpers for the Submit page. Extracted so the "is this month
/// already over, or is the user submitting early" question can be unit-tested without
/// standing up the Functions host or a DbContext — same pattern as the other classes
/// in this folder.
/// </summary>
public static class TimeSubmissionRules
{
    /// <summary>
    /// True when the given (year, month) has not yet fully elapsed as of <paramref name="utcNow"/>
    /// — i.e. it's the current calendar month or a future one. Used purely to label a
    /// submittable month as "early" in the UI; it is not itself a submission gate (the
    /// gate is "does the user have tracked time in that month", enforced against the DB
    /// in TimeSubmissionFunction.CreateAsync).
    /// </summary>
    public static bool IsEarlySubmission(int year, int month, DateTime utcNow)
    {
        var requested = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc);
        var currentMonthStart = new DateTime(utcNow.Year, utcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        return requested >= currentMonthStart;
    }

    /// <summary>
    /// Submit's "Your months" lists unsubmitted months with tracked time through
    /// <strong>next calendar month</strong> only. Far-future rows (imported weekly
    /// <c>[slug]</c> series) must not fill the page through 2040.
    /// </summary>
    public static bool IsListedOnSubmitPage(int year, int month, DateTime utcNow)
    {
        var requested = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc);
        var currentMonthStart = new DateTime(utcNow.Year, utcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var lastListable = currentMonthStart.AddMonths(1);
        return requested <= lastListable;
    }
}
