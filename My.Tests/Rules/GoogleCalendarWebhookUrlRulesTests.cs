using My.Shared.Rules;
using Xunit;

namespace My.Tests.Rules;

public class GoogleCalendarWebhookUrlRulesTests
{
    [Fact]
    public void Resolve_prefers_app_setting() =>
        Assert.Equal(
            "https://app.example.com/api/googlecalendar/webhook",
            GoogleCalendarWebhookUrlRules.Resolve(
                "https://app.example.com/api/googlecalendar/webhook",
                "func-example.azurewebsites.net"));

    [Fact]
    public void Resolve_uses_function_host_when_setting_missing() =>
        Assert.Equal(
            "https://func-example.azurewebsites.net/api/googlecalendar/webhook",
            GoogleCalendarWebhookUrlRules.Resolve(null, "func-example.azurewebsites.net"));

    [Fact]
    public void Resolve_null_when_neither_is_set() =>
        Assert.Null(GoogleCalendarWebhookUrlRules.Resolve("  ", null));

    [Fact]
    public void ResolveFromHttp_uses_request_host_before_function_host() =>
        Assert.Equal(
            "https://app.example.com/api/googlecalendar/webhook",
            GoogleCalendarWebhookUrlRules.ResolveFromHttp(
                null, "app.example.com", "https", "func-example.azurewebsites.net"));

    [Fact]
    public void ResolveFromHttp_falls_back_to_function_host() =>
        Assert.Equal(
            "https://func-example.azurewebsites.net/api/googlecalendar/webhook",
            GoogleCalendarWebhookUrlRules.ResolveFromHttp(null, null, "https", "func-example.azurewebsites.net"));

    [Theory]
    [InlineData("https://app.example.com/api/googlecalendar/webhook", true)]
    [InlineData("https://func.azurewebsites.net/api/googlecalendar/webhook", true)]
    [InlineData("https://localhost:7074/api/googlecalendar/webhook", false)]
    [InlineData("https://127.0.0.1/api/googlecalendar/webhook", false)]
    [InlineData("http://example.com/api/googlecalendar/webhook", false)]
    [InlineData(null, false)]
    public void IsReachableByGoogle_rejects_loopback_and_non_https(string? url, bool expected) =>
        Assert.Equal(expected, GoogleCalendarWebhookUrlRules.IsReachableByGoogle(url));

    [Fact]
    public void Source_reports_which_value_would_be_used()
    {
        Assert.Equal(
            GoogleCalendarWebhookUrlRules.SourceAppSetting,
            GoogleCalendarWebhookUrlRules.Source("https://example/webhook", "host"));
        Assert.Equal(
            GoogleCalendarWebhookUrlRules.SourceFunctionHost,
            GoogleCalendarWebhookUrlRules.Source(null, "host"));
        Assert.Equal(
            GoogleCalendarWebhookUrlRules.SourceMissing,
            GoogleCalendarWebhookUrlRules.Source("", "  "));
    }
}
