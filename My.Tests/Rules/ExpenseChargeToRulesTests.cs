using My.Shared.Rules;
using Xunit;

namespace My.Tests.Rules;

public class ExpenseChargeToRulesTests
{
    [Fact]
    public void Group_splits_lines_and_keeps_each_lines_place()
    {
        var lines = new[]
        {
            new Row("a", "Sample Organization", "Lunch"),
            new Row("b", "Beta", "Hotel"),
            new Row("a", "Sample Organization", "Taxi")
        };

        var groups = ExpenseChargeToRules.Group(lines, row => row.Id, row => row.Name, "Home Company");

        Assert.Equal(2, groups.Count);
        Assert.Equal(["Lunch", "Taxi"], groups.Single(group => group.Key == "a").Lines.Select(row => row.What));
        Assert.Equal("Beta", groups.Single(group => group.Key == "b").Name);
    }

    [Fact]
    public void Submit_requires_a_report_charge_to_and_a_missing_receipt_does_not_block()
    {
        Assert.Equal("This expense needs a Charge to.",
            ExpenseReportRules.SubmitBlockedReason(ExpenseStatusRules.Draft, 2, "  "));
        Assert.Null(ExpenseReportRules.SubmitBlockedReason(ExpenseStatusRules.Draft, 1, "org-1"));

        var gaps = ExpenseChargeToRules.MissingReceiptLabels(
        [
            (new DateTime(2026, 9, 29), "Claude Pro", 20m, 0),
            (new DateTime(2026, 9, 29), "Lunch", 12m, 1)
        ]);
        Assert.Single(gaps);
        Assert.Contains("Claude Pro", gaps[0]);
        Assert.DoesNotContain("Charge to", gaps[0], StringComparison.Ordinal);
    }

    [Fact]
    public void ReadQuery_blank_is_the_only_form_and_unassigned_is_the_empty_group()
    {
        Assert.Equal((false, ""), ExpenseChargeToRules.ReadQuery(null));
        Assert.Equal((false, ""), ExpenseChargeToRules.ReadQuery("  "));
        Assert.Equal((true, ""), ExpenseChargeToRules.ReadQuery(ExpenseChargeToRules.UnassignedQuery));
        Assert.Equal((true, "org-1"), ExpenseChargeToRules.ReadQuery(" org-1 "));
    }

    private sealed record Row(string Id, string Name, string What);
}
