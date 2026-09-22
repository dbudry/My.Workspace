using My.Shared.Rules;
using Xunit;

namespace My.Tests.Rules;

public class UserDeleteRetentionRulesTests
{
    [Fact]
    public void Recent_submit_blocks_delete()
    {
        var cutoff = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        Assert.True(UserDeleteRetentionRules.HasRecentExpenseActivity(
            cutoff,
            createdAt: cutoff.AddYears(-2),
            updatedAt: cutoff.AddYears(-2),
            submittedAt: cutoff.AddDays(1),
            reimbursedAt: null));
    }

    [Fact]
    public void Old_expenses_do_not_block_delete()
    {
        var cutoff = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        Assert.False(UserDeleteRetentionRules.HasRecentExpenseActivity(
            cutoff,
            createdAt: cutoff.AddDays(-1),
            updatedAt: cutoff.AddDays(-1),
            submittedAt: cutoff.AddDays(-10),
            reimbursedAt: cutoff.AddDays(-5)));
    }
}
