using My.Shared.Rules;
using Xunit;

namespace My.Tests.Rules;

public class ApiErrorBodyRulesTests
{
    [Fact]
    public void Format_joins_fluentvalidation_string_array()
    {
        var text = ApiErrorBodyRules.Format(
            """["Each line needs a description.","Cover period end must be on or after the start."]""",
            "fallback");
        Assert.Equal(
            "Each line needs a description. Cover period end must be on or after the start.",
            text);
    }

    [Fact]
    public void Format_unwraps_json_string()
    {
        Assert.Equal("You already have a report for that month.",
            ApiErrorBodyRules.Format("\"You already have a report for that month.\"", "fallback"));
    }

    [Fact]
    public void Format_plain_text_passthrough()
    {
        Assert.Equal("Expense report not found.",
            ApiErrorBodyRules.Format("Expense report not found.", "fallback"));
    }

    [Fact]
    public void Format_empty_uses_fallback()
    {
        Assert.Equal("Couldn't save.", ApiErrorBodyRules.Format("  ", "Couldn't save."));
        Assert.Equal("Couldn't save.", ApiErrorBodyRules.Format(null, "Couldn't save."));
    }
}
