using My.Shared.Rules;
using Xunit;

namespace My.Tests.Rules;

public class ExpenseListFilterRulesTests
{
    [Fact]
    public void PriorMonth_from_september_is_august_same_year()
    {
        var (year, month) = ExpenseListFilterRules.PriorMonth(new DateTime(2026, 9, 11));
        Assert.Equal(2026, year);
        Assert.Equal(8, month);
    }

    [Fact]
    public void PriorMonth_from_january_is_december_previous_year()
    {
        var (year, month) = ExpenseListFilterRules.PriorMonth(new DateTime(2027, 1, 3));
        Assert.Equal(2026, year);
        Assert.Equal(12, month);
    }

    [Fact]
    public void MatchesPeriod_empty_years_and_months_is_all()
    {
        Assert.True(ExpenseListFilterRules.MatchesPeriod(2026, 9, [], []));
    }

    [Fact]
    public void MatchesPeriod_requires_year_and_month_when_selected()
    {
        Assert.True(ExpenseListFilterRules.MatchesPeriod(2026, 9, [2026], [9]));
        Assert.False(ExpenseListFilterRules.MatchesPeriod(2025, 9, [2026], [9]));
        Assert.False(ExpenseListFilterRules.MatchesPeriod(2026, 8, [2026], [9]));
        Assert.True(ExpenseListFilterRules.MatchesPeriod(2026, 8, [2026, 2025], [8, 9]));
    }

    [Fact]
    public void CreatePeriod_uses_single_selection_otherwise_today()
    {
        var today = new DateTime(2026, 9, 11);
        Assert.Equal((2025, 3), ExpenseListFilterRules.CreatePeriod([2025], [3], today));
        Assert.Equal((2026, 9), ExpenseListFilterRules.CreatePeriod([2025, 2026], [3], today));
        Assert.Equal((2026, 9), ExpenseListFilterRules.CreatePeriod([], [], today));
    }

    [Fact]
    public void YearChoices_is_only_years_that_have_reports()
    {
        Assert.Empty(ExpenseListFilterRules.YearChoices([]));
        Assert.Equal([2026, 2024], ExpenseListFilterRules.YearChoices([2024, 2026, 2024]));
    }

    [Fact]
    public void ParseInts_splits_comma_list()
    {
        Assert.Equal([2026, 2025], ExpenseListFilterRules.ParseInts("2026, 2025"));
        Assert.Empty(ExpenseListFilterRules.ParseInts(null));
        Assert.Empty(ExpenseListFilterRules.ParseInts(""));
    }
}
