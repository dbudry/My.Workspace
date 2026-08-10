using My.Shared.Rules;
using Xunit;

namespace My.Tests.Rules;

public class DefaultStartTimeRulesTests
{
    [Fact]
    public void Default_is_eight_am()
    {
        Assert.Equal(8 * 60, DefaultStartTimeRules.DefaultMinutesPastMidnight);
        Assert.Equal(TimeSpan.FromHours(8), DefaultStartTimeRules.DefaultTimeOfDay);
    }

    [Theory]
    [InlineData(null, 8, 0)]
    [InlineData(-1, 8, 0)]
    [InlineData(0, 0, 0)]
    [InlineData(480, 8, 0)]
    [InlineData(9 * 60 + 30, 9, 30)]
    [InlineData(24 * 60, 23, 59)]
    public void Resolve_clamps_and_defaults(int? minutes, int hours, int mins)
    {
        Assert.Equal(new TimeSpan(hours, mins, 0), DefaultStartTimeRules.Resolve(minutes));
    }

    [Fact]
    public void FromTimeSpan_round_trip()
    {
        var t = TimeSpan.FromHours(8).Add(TimeSpan.FromMinutes(15));
        Assert.Equal(8 * 60 + 15, DefaultStartTimeRules.FromTimeSpan(t));
        Assert.Equal(t, DefaultStartTimeRules.Resolve(DefaultStartTimeRules.FromTimeSpan(t)));
    }
}
