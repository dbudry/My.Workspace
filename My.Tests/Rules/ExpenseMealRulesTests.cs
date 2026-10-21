using My.Shared.Rules;
using Xunit;

namespace My.Tests.Rules;

public class ExpenseMealRulesTests
{
    [Fact]
    public void Normalize_keeps_one_type_and_a_head_count()
    {
        var meal = ExpenseMealRules.Normalize("lunch", 4);
        Assert.Equal(ExpenseMealRules.Lunch, meal.MealType);
        Assert.Equal(4, meal.HeadCount);

        var columns = ExpenseMealRules.Columns(meal.MealType, meal.HeadCount);
        Assert.Null(columns.Breakfast);
        Assert.Equal(4, columns.Lunch);
        Assert.Null(columns.Dinner);
    }

    [Fact]
    public void Normalize_clears_a_type_without_people_and_people_without_a_type()
    {
        Assert.Equal((null, null), ExpenseMealRules.Normalize(ExpenseMealRules.Dinner, null));
        Assert.Equal((null, null), ExpenseMealRules.Normalize(null, 3));
        Assert.Equal((null, null), ExpenseMealRules.Normalize("brunch", 2));
    }
}
