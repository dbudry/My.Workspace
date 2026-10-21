using My.Shared.Rules;
using Xunit;

namespace My.Tests.Rules;

public class ExpenseForm8743RulesTests
{
    [Fact]
    public void MapLine_personal_car_uses_mileage_column_not_transport_amount()
    {
        var line = ExpenseForm8743Rules.MapLine(
            new DateTime(2026, 8, 12), "Client visit", ExpenseCategoryRules.Transportation,
            amount: 11.1m, miles: 20, transportationCode: "M",
            miscellaneousCode: null);

        Assert.Equal(20m, line.MileageMiles);
        Assert.Null(line.TransportAmount);
        Assert.Null(line.MiscAmount);
    }

    [Fact]
    public void MapLine_software_goes_to_misc_Z()
    {
        var line = ExpenseForm8743Rules.MapLine(
            new DateTime(2026, 8, 22), "SuperGrok Heavy", ExpenseCategoryRules.Software,
            amount: 99m, miles: null, transportationCode: null,
            miscellaneousCode: null);

        Assert.Equal("Z", line.MiscCode);
        Assert.Equal(99m, line.MiscAmount);
        Assert.Null(line.TelephoneAmount);
    }

    [Fact]
    public void MapLine_telephone_goes_to_telephone_column()
    {
        var line = ExpenseForm8743Rules.MapLine(
            new DateTime(2026, 9, 3), "ATT Phone", ExpenseCategoryRules.Telephone,
            amount: 75.57m, miles: null, transportationCode: null,
            miscellaneousCode: null);

        Assert.Equal(75.57m, line.TelephoneAmount);
        Assert.Null(line.MiscAmount);
    }

    [Fact]
    public void Totals_adds_mileage_dollars_from_rate()
    {
        var lines = new[]
        {
            ExpenseForm8743Rules.MapLine(
                DateTime.Today, "Miles", ExpenseCategoryRules.Transportation,
                0, 20, "M", null),
            ExpenseForm8743Rules.MapLine(
                DateTime.Today, "Grok", ExpenseCategoryRules.Software,
                99, null, null, null)
        };

        var totals = ExpenseForm8743Rules.Totals(lines, mileageRate: 0.555m);
        Assert.Equal(20m, totals.MileageMiles);
        Assert.Equal(11.10m, totals.MileageAmount);
        Assert.Equal(99m, totals.MiscAmount);
        Assert.Equal(110.10m, totals.GrandTotal);
    }

    [Fact]
    public void MapLine_meal_prints_headcount_in_one_column()
    {
        var line = ExpenseForm8743Rules.MapLine(
            new DateTime(2026, 9, 29), "Lunch", ExpenseCategoryRules.Meals,
            amount: 42m, miles: null, transportationCode: null, miscellaneousCode: null,
            mealType: ExpenseMealRules.Lunch, headCount: 4);

        Assert.Null(line.MealBreakfastCount);
        Assert.Equal(4, line.MealLunchCount);
        Assert.Null(line.MealDinnerCount);
        Assert.Equal(42m, line.MealsAmount);
        Assert.Equal("4", ExpenseMealRules.Cell(line.MealLunchCount));
        Assert.Equal("", ExpenseMealRules.Cell(line.MealBreakfastCount));
    }
}
