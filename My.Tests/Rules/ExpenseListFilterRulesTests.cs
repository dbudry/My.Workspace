using My.Shared.Dtos.Expenses;
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
    public void MatchesListYear_uses_cover_for_drafts_and_submit_year_when_filed()
    {
        var coverStart = new DateTime(2026, 8, 1);
        var coverEnd = new DateTime(2026, 8, 31);
        Assert.True(ExpenseListFilterRules.MatchesListYear(
            ExpenseStatusRules.Draft, 0, coverStart, coverEnd, 2026));
        Assert.False(ExpenseListFilterRules.MatchesListYear(
            ExpenseStatusRules.Draft, 0, coverStart, coverEnd, 2025));
        Assert.True(ExpenseListFilterRules.MatchesListYear(
            ExpenseStatusRules.Draft, 0, coverStart, coverEnd, null));
        Assert.True(ExpenseListFilterRules.MatchesListYear(
            ExpenseStatusRules.Submitted, 2026, coverStart, coverEnd, 2026));
        Assert.True(ExpenseListFilterRules.MatchesListYear(
            ExpenseStatusRules.Submitted, 2025, coverStart, coverEnd, 2026));
        Assert.True(ExpenseListFilterRules.MatchesListYear(
            ExpenseStatusRules.Submitted, 2025, coverStart, coverEnd, 2025));
        Assert.False(ExpenseListFilterRules.MatchesListYear(
            ExpenseStatusRules.Submitted, 2025, coverStart, coverEnd, 2024));
    }

    [Fact]
    public void MatchesListYear_spanning_cover_matches_start_year_and_end_year()
    {
        var coverStart = new DateTime(2026, 12, 1);
        var coverEnd = new DateTime(2027, 1, 14);
        Assert.True(ExpenseListFilterRules.MatchesListYear(
            ExpenseStatusRules.Draft, 0, coverStart, coverEnd, 2026));
        Assert.True(ExpenseListFilterRules.MatchesListYear(
            ExpenseStatusRules.Draft, 0, coverStart, coverEnd, 2027));
        Assert.False(ExpenseListFilterRules.MatchesListYear(
            ExpenseStatusRules.Draft, 0, coverStart, coverEnd, 2025));
        Assert.False(ExpenseListFilterRules.MatchesListYear(
            ExpenseStatusRules.Draft, 0, coverStart, coverEnd, 2028));
        Assert.True(ExpenseListFilterRules.MatchesListYear(
            ExpenseStatusRules.Draft, 0, new DateTime(2025, 11, 1), new DateTime(2027, 2, 1), 2026));
    }

    [Fact]
    public void YearsOnReport_uses_cover_years_and_adds_submit_year_when_filed()
    {
        var coverStart = new DateTime(2026, 9, 1);
        var coverEnd = new DateTime(2026, 9, 30);
        Assert.Equal([2026], ExpenseListFilterRules.YearsOnReport(
            ExpenseStatusRules.Draft, 0, coverStart, coverEnd));

        var spanStart = new DateTime(2026, 12, 1);
        var spanEnd = new DateTime(2027, 1, 14);
        Assert.Equal([2026, 2027], ExpenseListFilterRules.YearsOnReport(
            ExpenseStatusRules.Draft, 0, spanStart, spanEnd));
        Assert.Equal([2027, 2026], ExpenseListFilterRules.YearChoices(
            ExpenseListFilterRules.YearsOnReport(
                ExpenseStatusRules.Submitted, 2027, spanStart, spanEnd)));
    }

    [Fact]
    public void MatchesFilingMonth_keeps_drafts_and_filters_submitted()
    {
        Assert.True(ExpenseListFilterRules.MatchesFilingMonth(
            ExpenseStatusRules.Draft, 2026, 10, [2026], [9]));
        Assert.True(ExpenseListFilterRules.MatchesFilingMonth(
            ExpenseStatusRules.Submitted, 2026, 9, [2026], [9]));
        Assert.False(ExpenseListFilterRules.MatchesFilingMonth(
            ExpenseStatusRules.Reimbursed, 2026, 10, [2026], [9]));
        Assert.True(ExpenseListFilterRules.MatchesFilingMonth(
            ExpenseStatusRules.Submitted, 2026, 10, [], []));
    }

    [Fact]
    public void YearChoices_is_only_years_that_have_reports()
    {
        Assert.Empty(ExpenseListFilterRules.YearChoices([]));
        Assert.Equal([2026, 2024], ExpenseListFilterRules.YearChoices([2024, 2026, 2024]));
    }

    [Fact]
    public void MatchesStatuses_blank_is_all_and_selected_is_exact()
    {
        Assert.True(ExpenseListFilterRules.MatchesStatuses(ExpenseStatusRules.Draft, []));
        Assert.True(ExpenseListFilterRules.MatchesStatuses(ExpenseStatusRules.Draft, [ExpenseStatusRules.Draft]));
        Assert.False(ExpenseListFilterRules.MatchesStatuses(
            ExpenseStatusRules.Submitted, [ExpenseStatusRules.Draft, ExpenseStatusRules.Reimbursed]));
    }

    [Fact]
    public void MatchesSearch_finds_line_description_amount_date_and_receipt()
    {
        var row = new ExpenseReportListDto
        {
            EmployeeName = "Derek Budry",
            Status = ExpenseStatusRules.Draft,
            CoverStart = new DateTime(2026, 12, 1),
            CoverEnd = new DateTime(2027, 1, 14),
            TotalAmount = 86.40m,
            LineCount = 1,
            Purpose = "Customer visit",
            Lines =
            [
                new ExpenseLineSearchDto
                {
                    Date = new DateTime(2026, 12, 18),
                    Description = "Dinner with the account team",
                    Category = ExpenseCategoryRules.Meals,
                    Amount = 86.40m,
                    MealDinner = true,
                    ReceiptFileNames = ["hotel-folio.pdf"]
                }
            ]
        };

        Assert.True(ExpenseListFilterRules.MatchesSearch(row, null));
        Assert.True(ExpenseListFilterRules.MatchesSearch(row, "account team"));
        Assert.True(ExpenseListFilterRules.MatchesSearch(row, "$86.40"));
        Assert.True(ExpenseListFilterRules.MatchesSearch(row, "12/18/2026"));
        Assert.True(ExpenseListFilterRules.MatchesSearch(row, "hotel-folio"));
        Assert.True(ExpenseListFilterRules.MatchesSearch(row, "Dinner"));
        Assert.True(ExpenseListFilterRules.MatchesSearch(row, "Customer visit"));
        Assert.True(ExpenseListFilterRules.MatchesSearch(row, "1/14/2027"));
        Assert.False(ExpenseListFilterRules.MatchesSearch(row, "parking"));
    }

    [Fact]
    public void ParseInts_splits_comma_list()
    {
        Assert.Equal([2026, 2025], ExpenseListFilterRules.ParseInts("2026, 2025"));
        Assert.Empty(ExpenseListFilterRules.ParseInts(null));
        Assert.Empty(ExpenseListFilterRules.ParseInts(""));
    }
}
