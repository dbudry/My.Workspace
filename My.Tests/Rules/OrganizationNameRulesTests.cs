using My.Shared.Rules;
using Xunit;

namespace My.Tests.Rules;

public class OrganizationNameRulesTests
{
    [Theory]
    [InlineData("SampleOrg", "SampleOrg")]
    [InlineData("  SampleOrg  ", "SampleOrg")]
    [InlineData("SampleOrg", "SampleOrg")]
    public void NamesMatch_ignores_case_and_outer_whitespace(string a, string b)
    {
        Assert.True(OrganizationNameRules.NamesMatch(a, b));
    }

    [Theory]
    [InlineData("SampleOrg", "SampleOrg2")]
    [InlineData("SampleOrg", "")]
    [InlineData(null, "SampleOrg")]
    [InlineData("  ", "  ")]
    public void NamesMatch_false_for_different_or_empty(string? a, string? b)
    {
        Assert.False(OrganizationNameRules.NamesMatch(a, b));
    }

    [Fact]
    public void CollisionMessage_mentions_archived_when_existing_is_archived()
    {
        var msg = OrganizationNameRules.CollisionMessage("SampleOrg", existingIsArchived: true);
        Assert.Contains("SampleOrg", msg);
        Assert.Contains("archived", msg, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("unarchive", msg, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CollisionMessage_does_not_say_archived_for_live_collision()
    {
        var msg = OrganizationNameRules.CollisionMessage("SampleOrg", existingIsArchived: false);
        Assert.Contains("SampleOrg", msg);
        Assert.DoesNotContain("archived", msg, StringComparison.OrdinalIgnoreCase);
    }
}
