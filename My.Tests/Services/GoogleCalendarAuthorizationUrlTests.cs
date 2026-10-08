using Microsoft.Extensions.Logging.Abstractions;
using My.Functions.Services;
using My.Shared.Rules;
using Xunit;

namespace My.Tests.Services;

public class GoogleCalendarAuthorizationUrlTests
{
    [Fact]
    public void BuildAuthorizationUrl_is_calendar_only_and_has_no_login_hint()
    {
        var previousId = Environment.GetEnvironmentVariable("Google__ClientId");
        var previousSecret = Environment.GetEnvironmentVariable("Google__ClientSecret");
        try
        {
            Environment.SetEnvironmentVariable("Google__ClientId", "test-client-id.apps.googleusercontent.com");
            Environment.SetEnvironmentVariable("Google__ClientSecret", "test-secret");

            var service = new GoogleCalendarService(
                new GoogleTokenEncryptor(),
                NullLogger<GoogleCalendarService>.Instance);

            var url = service.BuildAuthorizationUrl("https://app.example.com/settings", "user-1", hostedDomain: "example.com");
            var decoded = Uri.UnescapeDataString(url);

            Assert.Contains(GoogleCalendarOAuthRules.CalendarScope, decoded, StringComparison.Ordinal);
            Assert.DoesNotContain("googleapis.com/auth/drive", decoded, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("login_hint=", decoded, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("hd=example.com", decoded, StringComparison.Ordinal);
            Assert.Contains("access_type=offline", decoded, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("prompt=consent", decoded, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("include_granted_scopes", decoded, StringComparison.OrdinalIgnoreCase);

            var reconnect = service.BuildAuthorizationUrl(
                "https://app.example.com/settings", "user-1", hostedDomain: "example.com", forceConsent: false);
            Assert.Contains("hd=example.com", Uri.UnescapeDataString(reconnect), StringComparison.Ordinal);
            var reconnectDecoded = Uri.UnescapeDataString(reconnect);
            Assert.Contains(GoogleCalendarOAuthRules.CalendarScope, reconnectDecoded, StringComparison.Ordinal);
            Assert.Contains("access_type=offline", reconnectDecoded, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("prompt=consent", reconnectDecoded, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("include_granted_scopes", reconnectDecoded, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Environment.SetEnvironmentVariable("Google__ClientId", previousId);
            Environment.SetEnvironmentVariable("Google__ClientSecret", previousSecret);
        }
    }

    [Fact]
    public void BuildAuthorizationUrl_omits_hd_when_hosted_domain_null()
    {
        var previousId = Environment.GetEnvironmentVariable("Google__ClientId");
        var previousSecret = Environment.GetEnvironmentVariable("Google__ClientSecret");
        try
        {
            Environment.SetEnvironmentVariable("Google__ClientId", "test-client-id.apps.googleusercontent.com");
            Environment.SetEnvironmentVariable("Google__ClientSecret", "test-secret");

            var service = new GoogleCalendarService(
                new GoogleTokenEncryptor(),
                NullLogger<GoogleCalendarService>.Instance);

            var url = service.BuildAuthorizationUrl("https://app.example.com/settings", "user-1");
            Assert.DoesNotContain("hd=", url, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Environment.SetEnvironmentVariable("Google__ClientId", previousId);
            Environment.SetEnvironmentVariable("Google__ClientSecret", previousSecret);
        }
    }
}
