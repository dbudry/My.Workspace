using My.Shared.Rules;
using Xunit;

namespace My.Tests.Rules;

public class ExpenseReportRulesTests
{
    [Fact]
    public void TryPeriodFromLineDates_same_month()
    {
        Assert.True(ExpenseReportRules.TryPeriodFromLineDates(
            [new DateTime(2026, 8, 10), new DateTime(2026, 8, 1, 15, 0, 0, DateTimeKind.Utc)],
            out var year, out var month));
        Assert.Equal(2026, year);
        Assert.Equal(8, month);
    }

    [Fact]
    public void TryPeriodFromLineDates_mixed_or_empty_is_false()
    {
        Assert.False(ExpenseReportRules.TryPeriodFromLineDates(
            [new DateTime(2026, 9, 9), new DateTime(2026, 8, 10)],
            out _, out _));
        Assert.False(ExpenseReportRules.TryPeriodFromLineDates([], out _, out _));
    }

    [Fact]
    public void CombineHomeAddress_joins_street_and_city()
    {
        Assert.Equal("2393 Viola Dr\nBay City MI 48706",
            ExpenseReportRules.CombineHomeAddress(" 2393 Viola Dr ", "Bay City MI 48706"));
        Assert.Equal(("", ""), ExpenseReportRules.SplitHomeAddress(null));
        Assert.Equal(("2393 Viola Dr", "Bay City MI 48706"),
            ExpenseReportRules.SplitHomeAddress("2393 Viola Dr\nBay City MI 48706"));
    }

    [Fact]
    public void IsLineDateInMonth_matches_calendar_month_only()
    {
        Assert.True(ExpenseReportRules.IsLineDateInMonth(new DateTime(2026, 8, 10), 2026, 8));
        Assert.False(ExpenseReportRules.IsLineDateInMonth(new DateTime(2026, 9, 9), 2026, 8));
        Assert.False(ExpenseReportRules.IsLineDateInMonth(new DateTime(2026, 8, 31), 2026, 9));
    }

    [Fact]
    public void DefaultCoverPeriod_is_first_through_last_of_month()
    {
        var (start, end) = ExpenseReportRules.DefaultCoverPeriod(2026, 2);
        Assert.Equal(new DateTime(2026, 2, 1), start);
        Assert.Equal(new DateTime(2026, 2, 28), end);
    }

    [Fact]
    public void MutateBlockedReason_when_submitted_or_reimbursed()
    {
        Assert.Null(ExpenseReportRules.MutateBlockedReason(ExpenseStatusRules.Draft));
        Assert.NotNull(ExpenseReportRules.MutateBlockedReason(ExpenseStatusRules.Submitted));
        Assert.NotNull(ExpenseReportRules.MutateBlockedReason(ExpenseStatusRules.Reimbursed));
    }

    [Fact]
    public void EmployeeDisplayName_trims_and_joins()
    {
        Assert.Equal("Derek Budry", ExpenseReportRules.EmployeeDisplayName("Derek", "Budry"));
    }

    [Fact]
    public void SubmitBlockedReason_requires_draft_with_lines()
    {
        Assert.Equal("Only a draft can be submitted.",
            ExpenseReportRules.SubmitBlockedReason(ExpenseStatusRules.Submitted, 2));
        Assert.Equal("Add at least one line before submitting.",
            ExpenseReportRules.SubmitBlockedReason(ExpenseStatusRules.Draft, 0));
        Assert.Null(ExpenseReportRules.SubmitBlockedReason(ExpenseStatusRules.Draft, 1));
    }

    [Fact]
    public void UnsubmitBlockedReason_only_when_submitted()
    {
        Assert.NotNull(ExpenseReportRules.UnsubmitBlockedReason(ExpenseStatusRules.Draft));
        Assert.Null(ExpenseReportRules.UnsubmitBlockedReason(ExpenseStatusRules.Submitted));
        Assert.NotNull(ExpenseReportRules.UnsubmitBlockedReason(ExpenseStatusRules.Reimbursed));
    }

    [Fact]
    public void ReimburseBlockedReason_only_when_submitted()
    {
        Assert.NotNull(ExpenseReportRules.ReimburseBlockedReason(ExpenseStatusRules.Draft));
        Assert.Null(ExpenseReportRules.ReimburseBlockedReason(ExpenseStatusRules.Submitted));
        Assert.NotNull(ExpenseReportRules.ReimburseBlockedReason(ExpenseStatusRules.Reimbursed));
    }

    [Fact]
    public void UndoReimburseBlockedReason_only_when_reimbursed()
    {
        Assert.NotNull(ExpenseReportRules.UndoReimburseBlockedReason(ExpenseStatusRules.Draft));
        Assert.NotNull(ExpenseReportRules.UndoReimburseBlockedReason(ExpenseStatusRules.Submitted));
        Assert.Null(ExpenseReportRules.UndoReimburseBlockedReason(ExpenseStatusRules.Reimbursed));
    }

    [Fact]
    public void CanView_owner_or_manager()
    {
        Assert.True(ExpenseReportRules.CanView("owner", "owner", isManager: false));
        Assert.False(ExpenseReportRules.CanView("owner", "other", isManager: false));
        Assert.True(ExpenseReportRules.CanView("owner", "other", isManager: true));
    }

    [Fact]
    public void IsValidCoverPeriod_start_after_end_is_invalid()
    {
        Assert.True(ExpenseReportRules.IsValidCoverPeriod(new DateTime(2026, 9, 1), new DateTime(2026, 9, 30)));
        Assert.True(ExpenseReportRules.IsValidCoverPeriod(new DateTime(2026, 9, 1), new DateTime(2026, 9, 1)));
        Assert.False(ExpenseReportRules.IsValidCoverPeriod(new DateTime(2026, 9, 15), new DateTime(2026, 9, 1)));
    }

    [Fact]
    public void IsLineDateAfterCoverEnd_flags_dates_past_cover_end()
    {
        var coverEnd = new DateTime(2026, 9, 30);
        Assert.True(ExpenseReportRules.IsLineDateAfterCoverEnd(new DateTime(2026, 10, 1), coverEnd));
        Assert.False(ExpenseReportRules.IsLineDateAfterCoverEnd(new DateTime(2026, 9, 30), coverEnd));
        Assert.False(ExpenseReportRules.IsLineDateAfterCoverEnd(new DateTime(2026, 9, 1), coverEnd));
        Assert.False(ExpenseReportRules.IsLineDateAfterCoverEnd(new DateTime(2026, 10, 1), default));
    }

    [Fact]
    public void IsOverdueDraft_prior_month_with_lines()
    {
        var today = new DateTime(2026, 9, 10);
        Assert.True(ExpenseReportRules.IsOverdueDraft(
            ExpenseStatusRules.Draft, 2026, 8, lineCount: 1, today));
        Assert.False(ExpenseReportRules.IsOverdueDraft(
            ExpenseStatusRules.Submitted, 2026, 8, lineCount: 1, today));
        Assert.False(ExpenseReportRules.IsOverdueDraft(
            ExpenseStatusRules.Draft, 2026, 9, lineCount: 1, today));
        Assert.False(ExpenseReportRules.IsOverdueDraft(
            ExpenseStatusRules.Draft, 2026, 8, lineCount: 0, today));
    }
}
