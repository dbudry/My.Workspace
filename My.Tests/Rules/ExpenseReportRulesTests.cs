using My.Shared.Rules;
using Xunit;

namespace My.Tests.Rules;

public class ExpenseReportRulesTests
{
    [Fact]
    public void SubmitMonth_uses_submitter_timezone_and_eastern_when_blank()
    {
        var utc = new DateTime(2026, 11, 1, 2, 30, 0, DateTimeKind.Utc);
        Assert.Equal((2026, 10), ExpenseReportRules.SubmitMonth(utc, "America/New_York"));
        Assert.Equal((2026, 10), ExpenseReportRules.SubmitMonth(utc, null));
        Assert.Equal((2026, 10), ExpenseReportRules.SubmitMonth(utc, "  "));

        var tokyo = new DateTime(2026, 10, 31, 15, 0, 0, DateTimeKind.Utc);
        Assert.Equal((2026, 11), ExpenseReportRules.SubmitMonth(tokyo, "Asia/Tokyo"));
    }

    [Fact]
    public void FilingMonthLabel_is_blank_until_submitted()
    {
        Assert.Equal("—", ExpenseReportRules.FilingMonthLabel(ExpenseStatusRules.Draft, 2026, 10));
        Assert.Equal("October 2026", ExpenseReportRules.FilingMonthLabel(ExpenseStatusRules.Submitted, 2026, 10));
        Assert.Equal("—", ExpenseReportRules.FilingMonthLabel(ExpenseStatusRules.Reimbursed, 0, 0));
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
        Assert.False(ExpenseReportRules.IsValidCoverPeriod(default, default));
    }

    [Fact]
    public void IsLineDateOutsideCover_flags_dates_outside_the_range()
    {
        var start = new DateTime(2026, 9, 1);
        var end = new DateTime(2026, 9, 30);
        Assert.True(ExpenseReportRules.IsLineDateOutsideCover(new DateTime(2026, 10, 1), start, end));
        Assert.True(ExpenseReportRules.IsLineDateOutsideCover(new DateTime(2026, 8, 31), start, end));
        Assert.False(ExpenseReportRules.IsLineDateOutsideCover(new DateTime(2026, 9, 30), start, end));
        Assert.False(ExpenseReportRules.IsLineDateOutsideCover(new DateTime(2026, 9, 1), start, end));
        Assert.False(ExpenseReportRules.IsLineDateOutsideCover(new DateTime(2026, 10, 1), default, default));
    }

    [Fact]
    public void IsOverdueDraft_when_cover_end_is_before_today()
    {
        var today = new DateTime(2026, 10, 7);
        Assert.True(ExpenseReportRules.IsOverdueDraft(
            ExpenseStatusRules.Draft, new DateTime(2026, 9, 30), lineCount: 1, today));
        Assert.False(ExpenseReportRules.IsOverdueDraft(
            ExpenseStatusRules.Submitted, new DateTime(2026, 9, 30), lineCount: 1, today));
        Assert.False(ExpenseReportRules.IsOverdueDraft(
            ExpenseStatusRules.Draft, new DateTime(2026, 10, 31), lineCount: 1, today));
        Assert.False(ExpenseReportRules.IsOverdueDraft(
            ExpenseStatusRules.Draft, new DateTime(2026, 9, 30), lineCount: 0, today));
    }
}
