using System.Globalization;
using My.Shared.Rules;
using Xunit;

namespace My.Tests.Rules;

public class DurationCsvRulesTests
{
    [Theory]
    [InlineData(0, "0.00")]
    [InlineData(1800, "0.50")]
    [InlineData(5400, "1.50")]
    [InlineData(9000, "2.50")]
    [InlineData(3600, "1.00")]
    [InlineData(60, "0.02")]
    [InlineData(900, "0.25")]
    [InlineData(90000, "25.00")]
    [InlineData(360000, "100.00")]
    public void ToDecimalHours_writes_two_places(double seconds, string expected) =>
        Assert.Equal(expected, DurationCsvRules.ToDecimalHours(seconds));

    [Theory]
    [InlineData(-30)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void ToDecimalHours_bad_input_is_zero(double seconds) =>
        Assert.Equal("0.00", DurationCsvRules.ToDecimalHours(seconds));

    [Fact]
    public void ToDecimalHours_uses_a_period_under_a_comma_culture()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            Assert.Equal("2.50", DurationCsvRules.ToDecimalHours(9000));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void ToDecimalHours_has_no_quote_or_colon_for_excel_to_misread()
    {
        var value = DurationCsvRules.ToDecimalHours(9000);
        Assert.DoesNotContain('"', value);
        Assert.DoesNotContain(':', value);
        Assert.True(double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed));
        Assert.Equal(2.5, parsed);
    }
}
