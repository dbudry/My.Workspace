using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.WebAssembly.Authentication;
using Microsoft.AspNetCore.Components.WebAssembly.Authentication.Internal;
using Microsoft.Extensions.Logging.Abstractions;
using My.Client.Services;
using My.Shared.Constants;
using My.Shared.Dtos.User;
using Xunit;

namespace My.Tests.Services;

/// <summary>
/// Covers cold-start provision recovery: retry on 5xx, no silent empty profile
/// without retries, and <see cref="CustomAccountFactory.IsMissingAppProfile"/>.
/// </summary>
public class CustomAccountFactoryTests
{
    public CustomAccountFactoryTests()
    {
        // Avoid multi-second backoff during unit tests.
        CustomAccountFactory.DelayAsync = static (_, _) => Task.CompletedTask;
    }

    [Fact]
    public void IsMissingAppProfile_true_when_authenticated_without_app_user_id()
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim(ClaimTypes.Email, "a@profitpt.com") },
            authenticationType: "GoogleOIDC"));

        Assert.True(CustomAccountFactory.IsMissingAppProfile(user));
    }

    [Fact]
    public void IsMissingAppProfile_false_when_app_user_id_present()
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity(
            new[]
            {
                new Claim(ClaimTypes.Email, "a@profitpt.com"),
                new Claim(Constants.Claims.AppUserId, "user-guid"),
            },
            authenticationType: "GoogleOIDC"));

        Assert.False(CustomAccountFactory.IsMissingAppProfile(user));
    }

    [Fact]
    public void IsMissingAppProfile_false_when_anonymous()
    {
        Assert.False(CustomAccountFactory.IsMissingAppProfile(new ClaimsPrincipal(new ClaimsIdentity())));
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError, true)]
    [InlineData(HttpStatusCode.RequestTimeout, true)]
    // Gateway codes are retried by RetryDelegatingHandler, not double-wrapped here.
    [InlineData(HttpStatusCode.BadGateway, false)]
    [InlineData(HttpStatusCode.ServiceUnavailable, false)]
    [InlineData(HttpStatusCode.GatewayTimeout, false)]
    [InlineData(HttpStatusCode.Unauthorized, false)]
    [InlineData(HttpStatusCode.Forbidden, false)]
    [InlineData(HttpStatusCode.BadRequest, false)]
    [InlineData(HttpStatusCode.OK, false)]
    public void IsTransientProvisionStatus_matches_cold_start_set(HttpStatusCode status, bool expected)
    {
        Assert.Equal(expected, CustomAccountFactory.IsTransientProvisionStatus(status));
    }

    [Fact]
    public async Task CreateUserAsync_retries_transient_500_then_applies_app_user_id()
    {
        var handler = new SequenceHandler(
            HttpStatusCode.InternalServerError,
            HttpStatusCode.InternalServerError,
            HttpStatusCode.OK);
        handler.SuccessBody = new UserDto
        {
            Id = "app-id-1",
            Email = "a@profitpt.com",
            Roles = new List<string> { "User:Tyme" },
        };

        var factory = CreateFactory(handler);
        var principal = await factory.CreateUserAsync(AuthenticatedAccount(), AuthOptions());

        Assert.Equal(3, handler.AttemptCount);
        Assert.Equal("app-id-1", principal.FindFirst(Constants.Claims.AppUserId)?.Value);
        Assert.Contains(principal.FindAll(ClaimTypes.Role), c => c.Value == "User:Tyme");
        Assert.False(CustomAccountFactory.IsMissingAppProfile(principal));
    }

    [Fact]
    public async Task CreateUserAsync_does_not_retry_forbidden()
    {
        var handler = new SequenceHandler(HttpStatusCode.Forbidden);
        var factory = CreateFactory(handler);
        var principal = await factory.CreateUserAsync(AuthenticatedAccount(), AuthOptions());

        Assert.Equal(1, handler.AttemptCount);
        Assert.True(CustomAccountFactory.IsMissingAppProfile(principal));
    }

    [Fact]
    public async Task CreateUserAsync_exhausted_retries_leaves_missing_profile()
    {
        // All attempts 500 — factory returns Google principal without app_user_id
        // so the dashboard can show Retry instead of an empty silent shell.
        var codes = Enumerable.Repeat(HttpStatusCode.InternalServerError, CustomAccountFactory.MaxProvisionRetries + 1)
            .ToArray();
        var handler = new SequenceHandler(codes);
        var factory = CreateFactory(handler);
        var principal = await factory.CreateUserAsync(AuthenticatedAccount(), AuthOptions());

        Assert.Equal(CustomAccountFactory.MaxProvisionRetries + 1, handler.AttemptCount);
        Assert.True(CustomAccountFactory.IsMissingAppProfile(principal));
    }

    private static CustomAccountFactory CreateFactory(HttpMessageHandler handler)
    {
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.test/") };
        return new CustomAccountFactory(
            new StubAccessTokenProviderAccessor(),
            new FixedHttpClientFactory(client),
            new StubNavigationManager(),
            new SessionExpiryService(),
            NullLogger<CustomAccountFactory>.Instance);
    }

    private static RemoteUserAccount AuthenticatedAccount() => new()
    {
        AdditionalProperties = new Dictionary<string, object>
        {
            // JsonElement is what the OIDC library stores; string fallback also works in some versions.
            ["email"] = JsonSerializer.SerializeToElement("a@profitpt.com"),
            ["sub"] = JsonSerializer.SerializeToElement("google-sub"),
            ["name"] = JsonSerializer.SerializeToElement("Test User"),
        }
    };

    private static RemoteAuthenticationUserOptions AuthOptions() => new()
    {
        AuthenticationType = "GoogleOIDC",
        NameClaim = "name",
        RoleClaim = "role",
    };

    private sealed class SequenceHandler : HttpMessageHandler
    {
        private readonly Queue<HttpStatusCode> _codes;
        public int AttemptCount { get; private set; }
        public UserDto? SuccessBody { get; set; }

        public SequenceHandler(params HttpStatusCode[] codes)
        {
            _codes = new Queue<HttpStatusCode>(codes);
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            AttemptCount++;
            var code = _codes.Count > 0 ? _codes.Dequeue() : HttpStatusCode.InternalServerError;
            var response = new HttpResponseMessage(code);
            if (code == HttpStatusCode.OK && SuccessBody != null)
                response.Content = JsonContent.Create(SuccessBody);
            return Task.FromResult(response);
        }
    }

    private sealed class FixedHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpClient _client;
        public FixedHttpClientFactory(HttpClient client) => _client = client;
        public HttpClient CreateClient(string name) => _client;
    }

    private sealed class StubAccessTokenProviderAccessor : IAccessTokenProviderAccessor
    {
        public IAccessTokenProvider TokenProvider { get; } = new StubAccessTokenProvider();
    }

    private sealed class StubAccessTokenProvider : IAccessTokenProvider
    {
        public ValueTask<AccessTokenResult> RequestAccessToken() =>
            new(new AccessTokenResult(AccessTokenResultStatus.Success, new AccessToken(), null, null));

        public ValueTask<AccessTokenResult> RequestAccessToken(AccessTokenRequestOptions options) =>
            RequestAccessToken();
    }

    private sealed class StubNavigationManager : NavigationManager
    {
        public StubNavigationManager() => Initialize("https://localhost:7047/", "https://localhost:7047/");
    }
}
