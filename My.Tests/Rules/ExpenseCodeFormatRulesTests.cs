using My.Shared.Rules;
using Xunit;

namespace My.Tests.Rules;

public class ExpenseCodeFormatRulesTests
{
    [Theory]
    [InlineData("A", true)]
    [InlineData("z", true)]
    [InlineData("A.00", true)]
    [InlineData("A.99", true)]
    [InlineData("b.07", true)]
    [InlineData("A.1", false)]
    [InlineData("A.100", false)]
    [InlineData("AA", false)]
    [InlineData("1", false)]
    [InlineData("", false)]
    public void Accepts_a_letter_or_letter_dot_two_digits(string code, bool valid) =>
        Assert.Equal(valid, ExpenseCodeFormatRules.IsValid(code));
}
