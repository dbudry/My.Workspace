using My.Shared.Rules;
using Xunit;

namespace My.Tests.Rules;

public class ExpensePublicLinkRulesTests
{
    [Fact]
    public void ReportUrl_uses_the_configured_host_and_falls_back_to_production()
    {
        Assert.Equal(
            "https://localhost:7047/expenses/abc",
            ExpensePublicLinkRules.ReportUrl("https://localhost:7047/", "abc"));
        Assert.Equal(
            "https://app.example.com/expenses/abc",
            ExpensePublicLinkRules.ReportUrl(null, "abc"));
        Assert.Equal(
            "https://app.example.com/expenses/abc",
            ExpensePublicLinkRules.ReportUrl("  ", "abc"));
    }
}
