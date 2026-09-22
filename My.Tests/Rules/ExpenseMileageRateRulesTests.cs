using My.Shared.Rules;
using Xunit;

namespace My.Tests.Rules;

public class ExpenseMileageRateRulesTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("nope")]
    [InlineData("-1")]
    [InlineData("11")]
    public void Parse_invalid_or_out_of_range_returns_default(string? raw)
    {
        Assert.Equal(ExpenseMileageRateRules.DefaultPerMile, ExpenseMileageRateRules.Parse(raw));
    }

    [Fact]
    public void Parse_invariant_decimal()
    {
        Assert.Equal(0.67m, ExpenseMileageRateRules.Parse("0.67"));
        Assert.Equal(0.555m, ExpenseMileageRateRules.Parse("0.555"));
        Assert.Equal(0m, ExpenseMileageRateRules.Parse("0"));
        Assert.Equal(10m, ExpenseMileageRateRules.Parse("10"));
    }

    [Fact]
    public void ToStorage_clamps_and_formats_invariant()
    {
        Assert.Equal("0.555", ExpenseMileageRateRules.ToStorage(0.555m));
        Assert.Equal("0.67", ExpenseMileageRateRules.ToStorage(0.67m));
        Assert.Equal("0", ExpenseMileageRateRules.ToStorage(-1m));
        Assert.Equal("10", ExpenseMileageRateRules.ToStorage(99m));
    }
}
