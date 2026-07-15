using My.Shared.Rules;
using Xunit;

namespace My.Tests.Rules;

public class GoogleCalendarWebhookRulesTests
{
    [Theory]
    [InlineData("abc", "abc", true)]
    [InlineData(" abc ", "abc", true)]
    [InlineData("wrong", "abc", false)]
    [InlineData(null, "abc", false)]
    [InlineData("abc", null, false)]
    [InlineData("", "abc", false)]
    public void IsChannelTokenValid_compares_trimmed_tokens(string? incoming, string? stored, bool expected) =>
        Assert.Equal(expected, GoogleCalendarWebhookRules.IsChannelTokenValid(incoming, stored));
}