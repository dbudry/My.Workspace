using My.Shared.Rules;
using Xunit;

namespace My.Tests.Rules;

public class ExpenseLineRulesTests
{
    [Fact]
    public void ComputeAmount_mileage_uses_rate_and_rounds_away_from_zero()
    {
        var amount = ExpenseLineRules.ComputeAmount(ExpenseCategoryRules.Mileage, 10m, 999m, 0.555m);
        Assert.Equal(5.55m, amount);
    }

    [Fact]
    public void ComputeAmount_personal_car_transport_uses_rate()
    {
        var amount = ExpenseLineRules.ComputeAmount(
            ExpenseCategoryRules.Transportation, 10m, 999m, 0.555m, ExpenseCategoryRules.PersonalCarCode);
        Assert.Equal(5.55m, amount);
    }

    [Fact]
    public void ComputeAmount_non_mileage_keeps_entered_amount()
    {
        var amount = ExpenseLineRules.ComputeAmount(ExpenseCategoryRules.Software, 10m, 24.4m, 0.555m);
        Assert.Equal(24.40m, amount);
    }

    [Fact]
    public void ComputeAmount_negative_clamps_to_zero()
    {
        Assert.Equal(0m, ExpenseLineRules.ComputeAmount(ExpenseCategoryRules.Hotel, null, -5m, 0.555m));
        Assert.Equal(0m, ExpenseLineRules.ComputeAmount(ExpenseCategoryRules.Mileage, -3m, 0m, 0.555m));
    }

    [Fact]
    public void ShiftIndicesAfterRemoval_drops_the_removed_index()
    {
        var indices = new HashSet<int> { 2 };
        ExpenseLineRules.ShiftIndicesAfterRemoval(indices, 2);
        Assert.Empty(indices);
    }

    [Fact]
    public void ShiftIndicesAfterRemoval_shifts_later_indices_down_by_one()
    {
        // Line at index 4 was flagged; removing the earlier line at index 1 must move
        // the flagged line's tracked index to 3 so the highlight follows the right row.
        var indices = new HashSet<int> { 4 };
        ExpenseLineRules.ShiftIndicesAfterRemoval(indices, 1);
        Assert.Equal(new HashSet<int> { 3 }, indices);
    }

    [Fact]
    public void ShiftIndicesAfterRemoval_leaves_earlier_indices_untouched()
    {
        var indices = new HashSet<int> { 0, 1 };
        ExpenseLineRules.ShiftIndicesAfterRemoval(indices, 3);
        Assert.Equal(new HashSet<int> { 0, 1 }, indices);
    }

    [Fact]
    public void ShiftIndicesAfterRemoval_handles_multiple_later_indices()
    {
        var indices = new HashSet<int> { 1, 3, 5 };
        ExpenseLineRules.ShiftIndicesAfterRemoval(indices, 2);
        Assert.Equal(new HashSet<int> { 1, 2, 4 }, indices);
    }
}
