using My.Shared.Rules;
using Xunit;

namespace My.Tests.Rules;

public class ProvisionFailureRulesTests
{
    [Theory]
    [InlineData(ProvisionFailureRules.CodeNotProvisioned)]
    [InlineData(ProvisionFailureRules.CodeInactiveOrArchived)]
    [InlineData(ProvisionFailureRules.CodeEmailNotAllowed)]
    [InlineData(ProvisionFailureRules.CodeServerError)]
    public void MessageFor_known_codes_is_non_empty(string code)
    {
        var message = ProvisionFailureRules.MessageFor(code);
        Assert.False(string.IsNullOrWhiteSpace(message));
        Assert.DoesNotContain("cold start", message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MessageFor_not_provisioned_mentions_admin()
    {
        var message = ProvisionFailureRules.MessageFor(ProvisionFailureRules.CodeNotProvisioned);
        Assert.Contains("administrator", message, StringComparison.OrdinalIgnoreCase);
    }
}
