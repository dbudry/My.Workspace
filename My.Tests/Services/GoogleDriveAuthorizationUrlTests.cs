using Microsoft.Extensions.Logging.Abstractions;
using My.Functions.Services;
using My.Shared.Rules;
using Xunit;

namespace My.Tests.Services;

public class GoogleDriveAuthorizationUrlTests
{
    [Fact]
    public void BuildAuthorizationUrl_requests_drive_incrementally_without_login_hint()
    {
        var previousId = Environment.GetEnvironmentVariable("Google__ClientId");
        var previousSecret = Environment.GetEnvironmentVariable("Google__ClientSecret");
        try
        {
            Environment.SetEnvironmentVariable("Google__ClientId", "test-client-id.apps.googleusercontent.com");
            Environment.SetEnvironmentVariable("Google__ClientSecret", "test-secret");

            var service = new GoogleDriveService(
                new GoogleTokenEncryptor(),
                NullLogger<GoogleDriveService>.Instance);

            var url = service.BuildAuthorizationUrl("https://app.example.com/settings", "user-1", hostedDomain: "example.com");
            var decoded = Uri.UnescapeDataString(url);

            Assert.Contains(GoogleDriveOAuthRules.DriveFileScope, decoded, StringComparison.Ordinal);
            Assert.Contains(GoogleDriveOAuthRules.DriveReadonlyScope, decoded, StringComparison.Ordinal);
            Assert.DoesNotContain(GoogleCalendarOAuthRules.CalendarScope, decoded, StringComparison.Ordinal);
            Assert.DoesNotContain("login_hint=", decoded, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("hd=example.com", decoded, StringComparison.Ordinal);
            Assert.Contains("include_granted_scopes=true", decoded, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("access_type=offline", decoded, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("prompt=consent", decoded, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Environment.SetEnvironmentVariable("Google__ClientId", previousId);
            Environment.SetEnvironmentVariable("Google__ClientSecret", previousSecret);
        }
    }
}
