using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Logging.Abstractions;
using My.Client.Services;
using Xunit;

namespace My.Tests.Services;

public class UserRoleRefreshServiceTests
{
    private sealed class CacheableAuthStateProvider : AuthenticationStateProvider
    {
        private DateTimeOffset _userLastCheck = DateTimeOffset.UtcNow;
        private ClaimsPrincipal _cachedUser = new(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Role, "Admin:Tyme") }, "test"));

        public override Task<AuthenticationState> GetAuthenticationStateAsync() =>
            Task.FromResult(new AuthenticationState(_cachedUser));

        public DateTimeOffset UserLastCheck => _userLastCheck;
        public ClaimsPrincipal CachedUser => _cachedUser;
    }

    [Fact]
    public void ResetOidcUserCache_clears_cached_principal_and_backdates_last_check()
    {
        var provider = new CacheableAuthStateProvider();

        var reset = UserRoleRefreshService.ResetOidcUserCache(provider, NullLogger.Instance);

        Assert.True(reset);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(0), provider.UserLastCheck);
        Assert.False(provider.CachedUser.Identity?.IsAuthenticated ?? true);
    }

    [Fact]
    public void ResetOidcUserCache_returns_false_when_fields_are_missing()
    {
        var provider = new AuthenticationStateProviderStub();

        var reset = UserRoleRefreshService.ResetOidcUserCache(provider, NullLogger.Instance);

        Assert.False(reset);
    }

    private sealed class AuthenticationStateProviderStub : AuthenticationStateProvider
    {
        public override Task<AuthenticationState> GetAuthenticationStateAsync() =>
            Task.FromResult(new AuthenticationState(new ClaimsPrincipal()));
    }
}