using My.Shared.Rules;
using Xunit;

namespace My.Tests.Rules;

public class CrmRulesTests
{
    [Theory]
    [InlineData("lead", "Lead")]
    [InlineData("WON", "Won")]
    [InlineData("Negotiation", "Negotiation")]
    public void Stage_accepts_known_values_ignoring_case(string input, string expected)
    {
        Assert.True(CrmStageRules.TryCanonical(input, out var canonical));
        Assert.Equal(expected, canonical);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Prospect")]
    public void Stage_rejects_unknown_values(string? input)
    {
        Assert.False(CrmStageRules.TryCanonical(input, out _));
    }

    [Theory]
    [InlineData("note", "Note")]
    [InlineData("TASK", "Task")]
    public void Activity_type_accepts_known_values(string input, string expected)
    {
        Assert.True(CrmActivityTypeRules.TryCanonical(input, out var canonical));
        Assert.Equal(expected, canonical);
    }

    [Fact]
    public void Activity_type_rejects_email()
    {
        Assert.False(CrmActivityTypeRules.TryCanonical("Email", out _));
    }
}
