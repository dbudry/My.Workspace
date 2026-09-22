using My.Shared.Rules;
using Xunit;

namespace My.Tests.Rules;

public class ExpenseCategoryRulesTests
{
    [Fact]
    public void IsKnown_accepts_closed_list()
    {
        Assert.True(ExpenseCategoryRules.IsKnown("Software"));
        Assert.True(ExpenseCategoryRules.IsKnown("Mileage"));
        Assert.False(ExpenseCategoryRules.IsKnown("Travel"));
        Assert.False(ExpenseCategoryRules.IsKnown(""));
        Assert.False(ExpenseCategoryRules.IsKnown(null));
    }

    [Fact]
    public void Normalize_is_case_insensitive_to_canonical()
    {
        Assert.Equal(ExpenseCategoryRules.Software, ExpenseCategoryRules.Normalize("software"));
        Assert.Equal("Nope", ExpenseCategoryRules.Normalize("Nope"));
    }

    [Fact]
    public void Personal_car_is_transportation_not_its_own_category()
    {
        Assert.DoesNotContain(ExpenseCategoryRules.CategoryChoices, c => c.Value == ExpenseCategoryRules.Mileage);
        Assert.Contains(ExpenseCategoryRules.CategoryChoices, c => c.Label == "Software / Subscription");
        Assert.True(ExpenseCategoryRules.IsPersonalCar(ExpenseCategoryRules.Transportation, "M"));
        Assert.True(ExpenseCategoryRules.IsPersonalCar(ExpenseCategoryRules.Mileage, null));
        Assert.False(ExpenseCategoryRules.IsPersonalCar(ExpenseCategoryRules.Transportation, "A"));
        var stored = ExpenseCategoryRules.ForStorage(ExpenseCategoryRules.Mileage, null);
        Assert.Equal(ExpenseCategoryRules.Transportation, stored.Category);
        Assert.Equal(ExpenseCategoryRules.PersonalCarCode, stored.TransportationCode);
    }

    [Fact]
    public void HasLineDetails_only_for_categories_with_extra_fields()
    {
        Assert.True(ExpenseCategoryRules.HasLineDetails(ExpenseCategoryRules.Mileage));
        Assert.True(ExpenseCategoryRules.HasLineDetails(ExpenseCategoryRules.Transportation));
        Assert.True(ExpenseCategoryRules.HasLineDetails(ExpenseCategoryRules.Miscellaneous));
        Assert.True(ExpenseCategoryRules.HasLineDetails(ExpenseCategoryRules.Meals));
        Assert.False(ExpenseCategoryRules.HasLineDetails(ExpenseCategoryRules.Software));
        Assert.False(ExpenseCategoryRules.HasLineDetails(ExpenseCategoryRules.Telephone));
        Assert.False(ExpenseCategoryRules.HasLineDetails(ExpenseCategoryRules.Hotel));
        Assert.False(ExpenseCategoryRules.HasLineDetails(ExpenseCategoryRules.Entertainment));
    }

    [Fact]
    public void Letter_codes_match_form_87_43()
    {
        Assert.True(ExpenseCategoryRules.IsTransportationCode("b"));
        Assert.True(ExpenseCategoryRules.IsTransportationCode("A"));
        Assert.False(ExpenseCategoryRules.IsTransportationCode("Z"));
        Assert.True(ExpenseCategoryRules.IsMiscellaneousCode("Z"));
        Assert.False(ExpenseCategoryRules.IsMiscellaneousCode("A"));
        Assert.Equal("Z", ExpenseCategoryRules.NormalizeCode("z"));
        Assert.Null(ExpenseCategoryRules.NormalizeCode(" "));
    }
}
