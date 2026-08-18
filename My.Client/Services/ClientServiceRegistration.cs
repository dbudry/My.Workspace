using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.WebAssembly.Authentication;
using Microsoft.AspNetCore.Components.WebAssembly.Authentication.Internal;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace My.Client.Services;

/// <summary>
/// Auth-related DI registrations extracted from <c>Program.cs</c> so they can be
/// mirrored in unit tests (see <c>ClientAuthDiTests</c>).
/// </summary>
public static class ClientServiceRegistration
{
    /// <summary>
    /// Registers role-refresh services. Call after OIDC auth and the inner
    /// <paramref name="oidcImplType"/> provider are registered in DI.
    /// </summary>
    public static IServiceCollection AddRoleRefreshServices(
        this IServiceCollection services,
        Type oidcImplType)
    {
        // AddAccountClaimsPrincipalFactory<T> registers T only as
        // AccountClaimsPrincipalFactory<RemoteUserAccount>. Pages and services that
        // need the concrete type (e.g. to invalidate the provision cache) must alias it.
        services.TryAddScoped<CustomAccountFactory>(sp =>
            (CustomAccountFactory)sp.GetRequiredService<AccountClaimsPrincipalFactory<RemoteUserAccount>>());

        services.TryAddScoped<OidcAuthenticationStateProvider>(sp =>
            new OidcAuthenticationStateProvider(
                (AuthenticationStateProvider)sp.GetRequiredService(oidcImplType)));

        services.TryAddScoped<IUserRoleRefreshService, UserRoleRefreshService>();

        return services;
    }
}