using My.Shared.Rules;
using Xunit;

namespace My.Tests.Rules;

public class CalendarChipRulesTests
{
    [Fact]
    public void FormatText_uses_details_when_present() =>
        Assert.Equal("Operations Meeting", CalendarChipRules.FormatText("  Operations Meeting ", "Admin"));

    [Fact]
    public void FormatText_falls_back_to_project_when_details_blank() =>
        Assert.Equal("Time Off - Vacation", CalendarChipRules.FormatText("", "Time Off - Vacation"));

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData("   ", "  ")]
    public void FormatText_untitled_when_both_blank(string? details, string? project) =>
        Assert.Equal(CalendarChipRules.UntitledFallback, CalendarChipRules.FormatText(details, project));
}
