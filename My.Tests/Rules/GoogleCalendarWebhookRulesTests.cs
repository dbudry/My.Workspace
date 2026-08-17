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

    [Theory]
    [InlineData("exists", true, "refresh", "primary", true)]
    [InlineData("EXISTS", true, "refresh", "primary", true)]
    [InlineData("sync", true, "refresh", "primary", false)]
    [InlineData("exists", false, "refresh", "primary", false)]
    [InlineData("exists", true, null, "primary", false)]
    [InlineData("exists", true, "refresh", null, false)]
    [InlineData("exists", true, "", "primary", false)]
    public void ShouldImport_requires_exists_and_live_credentials(
        string? state, bool enabled, string? token, string? calendarId, bool expected) =>
        Assert.Equal(expected, GoogleCalendarWebhookRules.ShouldImport(state, enabled, token, calendarId));

    [Fact]
    public void AcknowledgeEvenIfImportFails_is_true_so_google_does_not_retry() =>
        Assert.True(GoogleCalendarWebhookRules.AcknowledgeEvenIfImportFails);
}