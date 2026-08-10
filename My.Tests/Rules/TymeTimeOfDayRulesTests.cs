using My.Shared.Rules;
using Xunit;

namespace My.Tests.Rules;

public class TymeTimeOfDayRulesTests
{
    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("   ", true)]
    [InlineData("true", true)]
    [InlineData("True", true)]
    [InlineData("false", false)]
    [InlineData("0", false)]
    [InlineData("1", true)]
    [InlineData("no", false)]
    [InlineData("yes", true)]
    [InlineData("garbage", true)]
    public void ParseTrackTimeOfDay(string? raw, bool expected)
    {
        Assert.Equal(expected, TymeTimeOfDayRules.ParseTrackTimeOfDay(raw));
    }

    [Fact]
    public void Default_start_when_not_tracked_is_midnight()
    {
        Assert.Equal(TimeSpan.Zero, TymeTimeOfDayRules.DefaultStartTimeOfDayWhenNotTracked);
    }
}
