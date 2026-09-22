using System.Text;
using My.Shared.Rules;
using Xunit;

namespace My.Tests.Rules;

public class GoogleTeamAvailabilityCredentialRulesTests
{
    private const string SampleJson =
        """{"type":"service_account","project_id":"myprofitpoint","client_email":"tyme-team-availability@myprofitpoint.iam.gserviceaccount.com"}""";

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-json")]
    [InlineData("{ \"type\": \"authorized_user\" }")]
    public void TryResolveJson_rejects_empty_and_non_sa(string? raw)
    {
        Assert.False(GoogleTeamAvailabilityCredentialRules.TryResolveJson(raw, out var json));
        Assert.Equal("", json);
        Assert.False(GoogleTeamAvailabilityCredentialRules.IsConfigured(raw));
    }

    [Fact]
    public void TryResolveJson_accepts_raw_service_account_json()
    {
        Assert.True(GoogleTeamAvailabilityCredentialRules.TryResolveJson("  " + SampleJson + "\n", out var json));
        Assert.Contains("tyme-team-availability", json, StringComparison.Ordinal);
        Assert.True(GoogleTeamAvailabilityCredentialRules.IsConfigured(SampleJson));
    }

    [Fact]
    public void TryResolveJson_accepts_base64_of_json()
    {
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(SampleJson));
        var wrapped = encoded[..20] + "\n" + encoded[20..];
        Assert.True(GoogleTeamAvailabilityCredentialRules.TryResolveJson(wrapped, out var json));
        Assert.Equal(SampleJson, json);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("  ", null)]
    [InlineData(" dbudry@profitpt.com ", "dbudry@profitpt.com")]
    public void NormalizeImpersonateUser_trims_or_nulls(string? raw, string? expected) =>
        Assert.Equal(expected, GoogleTeamAvailabilityCredentialRules.NormalizeImpersonateUser(raw));
}
