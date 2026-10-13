using System.Net;
using System.Text;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using My.Client.Services;
using My.Shared.Constants;
using My.Shared.Rules;
using Xunit;

namespace My.Tests.Services;

public class UserSettingsGoogleConnectUrlTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InitiateGoogleConnect_requests_the_auth_url_and_records_forced_consent(bool forceConsent)
    {
        var js = new MemoryJs();
        var nav = new RecordingNav();
        var handler = new RecordingHandler();
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost:7047/api/") };
        var service = new UserSettingsService(new StubFactory(http), js, nav);

        await service.InitiateGoogleConnectAsync("https://localhost:7047/", forceConsent);

        var expected = GoogleOAuthRedirectRules.AppendRedirectUri(
            GoogleCalendarOAuthRules.CalendarClientAuthRoute(Constants.API.GoogleCalendar.GetAuthUrl, forceConsent),
            "https://localhost:7047/settings");
        Assert.Equal("/api/" + expected, handler.PathAndQuery);
        Assert.Equal("https://accounts.google.com/o/oauth2/v2/auth?client_id=test", nav.LastUri);

        var forced = await service.ConsumeGoogleCalendarConsentForcedAsync();
        Assert.Equal(forceConsent, forced);
        Assert.False(await service.ConsumeGoogleCalendarConsentForcedAsync());
    }

    [Fact]
    public async Task Normal_connect_clears_a_consent_flag_left_by_a_closed_tab()
    {
        var js = new MemoryJs();
        js.Set(UserSettingsService.GoogleCalendarConsentForcedKey, "true");
        var handler = new RecordingHandler();
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost:7047/api/") };
        var service = new UserSettingsService(new StubFactory(http), js, new RecordingNav());

        await service.InitiateGoogleConnectAsync(forceConsent: false);

        Assert.False(await service.ConsumeGoogleCalendarConsentForcedAsync());
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public string? PathAndQuery { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            PathAndQuery = request.RequestUri?.PathAndQuery;
            var body = """{"url":"https://accounts.google.com/o/oauth2/v2/auth?client_id=test"}""";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class StubFactory : IHttpClientFactory
    {
        private readonly HttpClient _client;
        public StubFactory(HttpClient client) => _client = client;
        public HttpClient CreateClient(string name) => _client;
    }

    private sealed class MemoryJs : IJSRuntime
    {
        private readonly Dictionary<string, string?> _store = new(StringComparer.Ordinal);

        public void Set(string key, string value) => _store[key] = value;

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            if (identifier == "localStorage.getItem")
            {
                var key = (string)args![0]!;
                _store.TryGetValue(key, out var value);
                return ValueTask.FromResult((TValue)(object?)value!);
            }

            if (identifier == "localStorage.setItem")
                _store[(string)args![0]!] = args[1]?.ToString();
            else if (identifier == "localStorage.removeItem")
                _store.Remove((string)args![0]!);

            return ValueTask.FromResult(default(TValue)!);
        }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
            InvokeAsync<TValue>(identifier, args);
    }

    private sealed class RecordingNav : NavigationManager
    {
        public RecordingNav() => Initialize("https://localhost:7047/", "https://localhost:7047/settings");

        public string? LastUri { get; private set; }

        protected override void NavigateToCore(string uri, NavigationOptions options) => LastUri = uri;
    }
}
