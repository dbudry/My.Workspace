using My.Shared.Rules;
using Xunit;

namespace My.Tests.Rules;

public class GoogleOAuthRedirectRulesTests
{
    [Theory]
    [InlineData("https://localhost:7047/settings")]
    [InlineData("https://localhost:9999/settings")]
    [InlineData("https://your-app.example.com/settings")]
    [InlineData("https://zealous-grass-01d92eb0f.7.azurestaticapps.net/settings")]
    public void IsAllowedRedirectUri_accepts_known_origins(string uri) =>
        Assert.True(GoogleOAuthRedirectRules.IsAllowedRedirectUri(uri, out _));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("http://localhost:7047/settings")]
    [InlineData("https://evil.example.com/settings")]
    [InlineData("https://localhost:7047/oauth/callback")]
    [InlineData("https://your-app.example.com/settings?x=1")]
    public void IsAllowedRedirectUri_rejects_invalid_uris(string? uri)
    {
        Assert.False(GoogleOAuthRedirectRules.IsAllowedRedirectUri(uri, out var error));
        Assert.False(string.IsNullOrWhiteSpace(error));
    }
}