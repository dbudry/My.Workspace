using My.Shared.Validation;
using Xunit;

namespace My.Tests.Validation;

public class SlugRulesTests
{
    [Theory]
    [InlineData(null, true)]
    [InlineData("ab", true)]
    [InlineData("abcde", true)]
    [InlineData("abcdefghij", true)] // 10 = max
    [InlineData("abcdefghijk", false)] // 11 over max
    [InlineData("a", false)] // under min
    [InlineData("ab-c", false)]
    [InlineData("AB", false)] // Normalize not applied here — shape expects lower
    [InlineData("ab12", true)]
    public void IsValidShape_enforces_length_and_charset(string? slug, bool expected)
    {
        Assert.Equal(expected, SlugRules.IsValidShape(slug));
    }

    [Fact]
    public void Normalize_lowercases_and_trims()
    {
        Assert.Equal("itsecurity", SlugRules.Normalize("  ITSecurity  "));
        Assert.True(SlugRules.IsValidShape(SlugRules.Normalize("ITSecurity")));
    }

    [Fact]
    public void MaxLength_is_10()
    {
        Assert.Equal(10, SlugRules.MaxLength);
        Assert.Equal(2, SlugRules.MinLength);
    }
}
