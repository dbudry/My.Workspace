using My.Shared.Rules;
using Xunit;

namespace My.Tests.Rules;

public class GoogleIdentityRulesTests
{
    private const string SingleDomain = "example.com";
    private const string MultiDomains = "example.com, contoso.org";
    private const string AllowAny = "*";

    [Theory]
    [InlineData("user@example.com", SingleDomain, true)]
    [InlineData("User@Example.COM", SingleDomain, true)]
    [InlineData("user@gmail.com", SingleDomain, false)]
    [InlineData("not-an-email", SingleDomain, false)]
    [InlineData(null, SingleDomain, false)]
    [InlineData("user@example.com", "", false)]
    [InlineData("user@example.com", null, false)]
    public void IsAllowedEmail_respects_configured_domains(string? email, string? domains, bool expected) =>
        Assert.Equal(expected, GoogleIdentityRules.IsAllowedEmail(email, domains));

    [Theory]
    [InlineData("a@gmail.com", AllowAny, true)]
    [InlineData("b@company.io", "any", true)]
    [InlineData("c@x.com", "all", true)]
    public void IsAllowedEmail_allow_any_domain(string email, string domains, bool expected) =>
        Assert.Equal(expected, GoogleIdentityRules.IsAllowedEmail(email, domains));

    [Fact]
    public void IsAllowedEmail_multi_domain()
    {
        Assert.True(GoogleIdentityRules.IsAllowedEmail("a@example.com", MultiDomains));
        Assert.True(GoogleIdentityRules.IsAllowedEmail("b@contoso.org", MultiDomains));
        Assert.False(GoogleIdentityRules.IsAllowedEmail("c@gmail.com", MultiDomains));
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("True", true)]
    [InlineData("false", false)]
    [InlineData(null, false)]
    [InlineData("", false)]
    public void IsEmailVerified_requires_true(string? claim, bool expected) =>
        Assert.Equal(expected, GoogleIdentityRules.IsEmailVerified(claim));

    [Fact]
    public void IsAllowedGoogleIdentity_requires_both_checks()
    {
        Assert.True(GoogleIdentityRules.IsAllowedGoogleIdentity("a@example.com", "true", SingleDomain));
        Assert.False(GoogleIdentityRules.IsAllowedGoogleIdentity("a@example.com", "false", SingleDomain));
        Assert.False(GoogleIdentityRules.IsAllowedGoogleIdentity("a@gmail.com", "true", SingleDomain));
    }

    [Fact]
    public void GetSingleHostedDomainHint_only_for_exactly_one_domain()
    {
        Assert.Equal("example.com", GoogleIdentityRules.GetSingleHostedDomainHint(SingleDomain));
        Assert.Null(GoogleIdentityRules.GetSingleHostedDomainHint(MultiDomains));
        Assert.Null(GoogleIdentityRules.GetSingleHostedDomainHint(AllowAny));
        Assert.Null(GoogleIdentityRules.GetSingleHostedDomainHint(""));
    }

    [Fact]
    public void ParseAllowedDomains_normalizes()
    {
        var parsed = GoogleIdentityRules.ParseAllowedDomains(" @Example.com, CONTOSO.org ;foo.io ");
        Assert.Equal(new[] { "example.com", "contoso.org", "foo.io" }, parsed);
    }

    [Fact]
    public void CoerceEmailVerified_from_bool_and_string()
    {
        Assert.Equal("true", GoogleIdentityRules.CoerceEmailVerified(true));
        Assert.Equal("false", GoogleIdentityRules.CoerceEmailVerified(false));
        Assert.Null(GoogleIdentityRules.CoerceEmailVerified((bool?)null));
        Assert.Equal("true", GoogleIdentityRules.CoerceEmailVerified(" true "));
        Assert.Null(GoogleIdentityRules.CoerceEmailVerified("  "));
    }

    /// <summary>
    /// Mirrors AuthMiddleware tokeninfo deserialization: Google returns email_verified as a JSON bool.
    /// A string property used to throw and reject every SPA access_token.
    /// </summary>
    [Fact]
    public void Tokeninfo_json_with_boolean_email_verified_deserializes()
    {
        const string json =
            """{"sub":"abc","email":"user@example.com","email_verified":true,"aud":"client-id"}""";

        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal(System.Text.Json.JsonValueKind.True, root.GetProperty("email_verified").ValueKind);

        var emailVerified = root.GetProperty("email_verified").ValueKind switch
        {
            System.Text.Json.JsonValueKind.True => "true",
            System.Text.Json.JsonValueKind.False => "false",
            System.Text.Json.JsonValueKind.String => root.GetProperty("email_verified").GetString(),
            _ => null
        };

        Assert.Equal("true", emailVerified);
        Assert.True(GoogleIdentityRules.IsAllowedGoogleIdentity(
            root.GetProperty("email").GetString(), emailVerified, SingleDomain));
    }
}
