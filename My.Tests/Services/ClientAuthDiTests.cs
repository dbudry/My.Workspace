using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.WebAssembly.Authentication;
using Microsoft.AspNetCore.Components.WebAssembly.Authentication.Internal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using My.Client.Services;
using Xunit;

namespace My.Tests.Services;

/// <summary>
/// Mirrors <see cref="ClientServiceRegistration"/> so a missing registration
/// (e.g. CustomAccountFactory not aliased) fails in CI instead of at runtime on
/// /admin/users.
/// </summary>
public class ClientAuthDiTests
{
    private sealed class StubOidcAuthStateProvider : AuthenticationStateProvider
    {
        private readonly DateTimeOffset _userLastCheck = DateTimeOffset.FromUnixTimeSeconds(0);
        private readonly ClaimsPrincipal _cachedUser = new(new ClaimsIdentity());

        public override Task<AuthenticationState> GetAuthenticationStateAsync() =>
            Task.FromResult(new AuthenticationState(_cachedUser));
    }

    private static ServiceProvider BuildProvider(bool registerFactory = true)
    {
        var services = new ServiceCollection();
        services.AddLogging();

        if (registerFactory)
        {
            services.AddScoped<CustomAccountFactory>(_ => new ThrowingAccountFactory());
            services.AddScoped<AccountClaimsPrincipalFactory<RemoteUserAccount>>(sp =>
                sp.GetRequiredService<CustomAccountFactory>());
        }

        services.AddScoped<StubOidcAuthStateProvider>();
        services.AddScoped<AuthenticationStateProvider>(sp => sp.GetRequiredService<StubOidcAuthStateProvider>());
        services.AddScoped(_ => new LocalStorageService(null!));
        services.AddScoped<ImpersonationService>();
        services.AddScoped<ImpersonationAuthStateProvider>(sp =>
            new ImpersonationAuthStateProvider(
                sp.GetRequiredService<AuthenticationStateProvider>(),
                sp.GetRequiredService<ImpersonationService>()));

        services.AddRoleRefreshServices(typeof(StubOidcAuthStateProvider));

        return services.BuildServiceProvider();
    }

    [Fact]
    public void Role_refresh_services_resolve_when_factory_is_registered()
    {
        using var provider = BuildProvider(registerFactory: true);
        using var scope = provider.CreateScope();

        var factory = scope.ServiceProvider.GetRequiredService<CustomAccountFactory>();
        var refresh = scope.ServiceProvider.GetRequiredService<IUserRoleRefreshService>();
        var oidcHolder = scope.ServiceProvider.GetRequiredService<OidcAuthenticationStateProvider>();

        Assert.IsType<ThrowingAccountFactory>(factory);
        Assert.IsType<UserRoleRefreshService>(refresh);
        Assert.IsType<StubOidcAuthStateProvider>(oidcHolder.Inner);
    }

    [Fact]
    public void CustomAccountFactory_alias_points_at_same_instance_as_principal_factory()
    {
        using var provider = BuildProvider(registerFactory: true);
        using var scope = provider.CreateScope();

        var concrete = scope.ServiceProvider.GetRequiredService<CustomAccountFactory>();
        var principal = scope.ServiceProvider.GetRequiredService<AccountClaimsPrincipalFactory<RemoteUserAccount>>();

        Assert.Same(concrete, principal);
    }

    [Fact]
    public void CustomAccountFactory_throws_when_principal_factory_not_registered()
    {
        using var provider = BuildProvider(registerFactory: false);
        using var scope = provider.CreateScope();

        Assert.Throws<InvalidOperationException>(() =>
            scope.ServiceProvider.GetRequiredService<CustomAccountFactory>());
    }

    /// <summary>Stand-in — never invoked by these tests; only proves DI wiring.</summary>
    private sealed class ThrowingAccountFactory : CustomAccountFactory
    {
        public ThrowingAccountFactory()
            : base(
                new FakeAccessTokenProviderAccessor(),
                new FakeHttpClientFactory(),
                new FakeNavigationManager(),
                new SessionExpiryService(),
                Microsoft.Extensions.Logging.Abstractions.NullLogger<CustomAccountFactory>.Instance)
        {
        }
    }

    private sealed class FakeAccessTokenProviderAccessor : IAccessTokenProviderAccessor
    {
        public IAccessTokenProvider TokenProvider => throw new NotImplementedException();
    }

    private sealed class FakeHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => throw new NotImplementedException();
    }

    private sealed class FakeNavigationManager : Microsoft.AspNetCore.Components.NavigationManager
    {
        public FakeNavigationManager() => Initialize("https://localhost:7047/", "https://localhost:7047/");
    }
}