using My.Shared.Rules;
using Xunit;

namespace My.Tests.Rules;

public class TrackedTaskLockRulesTests
{
    [Fact]
    public void Submitted_month_message_says_why_and_where()
    {
        Assert.Contains("submitted month", TrackedTaskLockRules.SubmittedMonthMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Submit", TrackedTaskLockRules.SubmittedMonthMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Submit_path_matches_the_submit_page() =>
        Assert.Equal("tyme/submit", TrackedTaskLockRules.SubmitPagePath);
}
