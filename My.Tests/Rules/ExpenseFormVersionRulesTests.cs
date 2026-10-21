using My.Shared.Rules;
using Xunit;

namespace My.Tests.Rules;

public class ExpenseFormVersionRulesTests
{
    // Current is the form-change gate. When the generated form changes, raise
    // ExpenseFormVersionRules.Current and this expected number together.
    [Fact]
    public void Current_is_2_after_the_currency_footer()
    {
        Assert.Equal(2, ExpenseFormVersionRules.Current);
        Assert.Equal("Version 2", ExpenseFormVersionRules.Label);
    }
}
