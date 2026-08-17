using My.Shared.Rules;
using Xunit;

namespace My.Tests.Rules;

public class OrganizationNameRulesTests
{
    [Theory]
    [InlineData("Acme", "acme")]
    [InlineData("  Acme  ", "ACME")]
    [InlineData("Acme", "Acme")]
    public void NamesMatch_ignores_case_and_outer_whitespace(string a, string b)
    {
        Assert.True(OrganizationNameRules.NamesMatch(a, b));
    }

    [Theory]
    [InlineData("Acme", "Acme2")]
    [InlineData("Acme", "")]
    [InlineData(null, "Acme")]
    [InlineData("  ", "  ")]
    public void NamesMatch_false_for_different_or_empty(string? a, string? b)
    {
        Assert.False(OrganizationNameRules.NamesMatch(a, b));
    }

    [Fact]
    public void CollisionMessage_mentions_archived_when_existing_is_archived()
    {
        var msg = OrganizationNameRules.CollisionMessage("Acme", existingIsArchived: true);
        Assert.Contains("Acme", msg);
        Assert.Contains("archived", msg, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("unarchive", msg, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CollisionMessage_does_not_say_archived_for_live_collision()
    {
        var msg = OrganizationNameRules.CollisionMessage("Acme", existingIsArchived: false);
        Assert.Contains("Acme", msg);
        Assert.DoesNotContain("archived", msg, StringComparison.OrdinalIgnoreCase);
    }
}
