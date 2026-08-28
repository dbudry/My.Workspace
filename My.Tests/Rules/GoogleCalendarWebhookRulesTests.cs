using My.Shared.Constants;
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

    [Theory]
    [InlineData("ch-1", "exists", true)]
    [InlineData("ch-1", "EXISTS", true)]
    [InlineData("ch-1", "sync", false)]
    [InlineData("", "exists", false)]
    [InlineData(null, "exists", false)]
    [InlineData("ch-1", null, false)]
    public void ShouldEnqueue_only_exists_with_a_channel(
        string? channelId, string? state, bool expected) =>
        Assert.Equal(expected, GoogleCalendarWebhookRules.ShouldEnqueue(channelId, state));

    [Fact]
    public void IsProbeChannel_matches_admin_sentinel()
    {
        Assert.True(GoogleCalendarWebhookRules.IsProbeChannel(Constants.API.GoogleCalendar.ProbeChannelId));
        Assert.False(GoogleCalendarWebhookRules.IsProbeChannel("ch-1"));
    }

    [Fact]
    public void AcknowledgeEvenIfImportFails_is_true_so_handshake_does_not_retry() =>
        Assert.True(GoogleCalendarWebhookRules.AcknowledgeEvenIfImportFails);

    [Theory]
    [InlineData(0, false)]
    [InlineData(4, false)]
    [InlineData(5, true)]
    [InlineData(8, true)]
    public void IsApproachingPoison_from_fifth_dequeue(int count, bool expected) =>
        Assert.Equal(expected, GoogleCalendarWebhookRules.IsApproachingPoison(count));

    [Fact]
    public void ImportQueue_is_a_valid_azure_queue_name()
    {
        var name = Constants.API.GoogleCalendar.ImportQueue;
        Assert.Equal("google-calendar-import", name);
        Assert.InRange(name.Length, 3, 63);
        Assert.Matches("^[a-z0-9]([a-z0-9-]*[a-z0-9])?$", name);
    }

    [Theory]
    [InlineData(true, null, true)]
    [InlineData(true, "", true)]
    [InlineData(true, "next-token", false)]
    [InlineData(false, null, false)]
    [InlineData(false, "next-token", false)]
    public void ShouldResyncWithoutToken_when_a_stored_token_returns_no_next(
        bool hadToken, string? next, bool expected) =>
        Assert.Equal(expected, GoogleCalendarWebhookRules.ShouldResyncWithoutToken(hadToken, next));
}