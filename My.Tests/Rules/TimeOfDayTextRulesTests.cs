using My.Shared.Rules;
using Xunit;

namespace My.Tests.Rules;

public class TimeOfDayTextRulesTests
{
    [Theory]
    [InlineData("9:30 AM", true)]
    [InlineData("9:30AM", true)]
    [InlineData("14:30", true)]
    [InlineData("9", true)]
    [InlineData("9pm", true)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("not-a-time", false)]
    [InlineData("abc", false)]
    // Bare hour + single-letter meridiem ("9P", "12A") — hits the manual meridiem
    // branch in TryParse rather than the DateTime.TryParseExact format list.
    [InlineData("9P", true)]
    [InlineData("12A", true)]
    // Boundary hours for the bare-hour fast path.
    [InlineData("0", true)]
    [InlineData("23", true)]
    [InlineData("24", false)]
    [InlineData("25", false)]
    [InlineData("-1", false)]
    // 12-hour edge cases: 12AM is midnight, 12PM is noon.
    [InlineData("12am", true)]
    [InlineData("12pm", true)]
    public void TryParse_accepts_common_and_rejects_garbage(string input, bool ok)
    {
        var parsed = TimeOfDayTextRules.TryParse(input, out _);
        Assert.Equal(ok, parsed);
    }

    [Fact]
    public void TryParse_9P_means_9_pm()
    {
        Assert.True(TimeOfDayTextRules.TryParse("9P", out var ts));
        Assert.Equal(new TimeSpan(21, 0, 0), ts);
    }

    [Fact]
    public void TryParse_12A_is_midnight_not_noon()
    {
        Assert.True(TimeOfDayTextRules.TryParse("12A", out var ts));
        Assert.Equal(TimeSpan.Zero, ts);
    }

    [Fact]
    public void TryParse_12am_is_midnight()
    {
        Assert.True(TimeOfDayTextRules.TryParse("12am", out var ts));
        Assert.Equal(TimeSpan.Zero, ts);
    }

    [Fact]
    public void TryParse_12pm_is_noon()
    {
        Assert.True(TimeOfDayTextRules.TryParse("12pm", out var ts));
        Assert.Equal(new TimeSpan(12, 0, 0), ts);
    }

    [Fact]
    public void TryParse_falls_back_to_culture_parse_for_formats_outside_the_explicit_list()
    {
        // "09:30:00" isn't in the explicit format list (no seconds pattern) and doesn't
        // end in a meridiem letter, so this only succeeds via the final
        // DateTime.TryParse(..., CultureInfo.CurrentCulture, ...) fallback branch.
        Assert.True(TimeOfDayTextRules.TryParse("09:30:00", out var ts));
        Assert.Equal(new TimeSpan(9, 30, 0), ts);
    }

    [Fact]
    public void Validate_never_throws_on_garbage()
    {
        var err = TimeOfDayTextRules.Validate("@@@@", use24Hour: true, out var ts);
        Assert.NotNull(err);
        Assert.Equal(TimeSpan.Zero, ts);
    }

    [Fact]
    public void Validate_empty_is_required()
    {
        var err = TimeOfDayTextRules.Validate("  ", use24Hour: false, out _);
        Assert.Equal(TimeOfDayTextRules.RequiredMessage, err);
    }
}
