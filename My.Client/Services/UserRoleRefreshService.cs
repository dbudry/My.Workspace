using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Logging;

namespace My.Client.Services;

/// <summary>
/// Rebuilds the WASM auth principal after a self-service role change. Must bust both
/// <see cref="CustomAccountFactory"/>'s provision cache and
/// RemoteAuthenticationService's 60s <c>_cachedUser</c> — notifying auth state alone
/// is insufficient because the framework returns the stale principal from cache.
/// </summary>
public sealed class UserRoleRefreshService : IUserRoleRefreshService
{
    private readonly CustomAccountFactory _accountFactory;
    private readonly OidcAuthenticationStateProvider _oidcAuth;
    private readonly ImpersonationAuthStateProvider _authStateProvider;
    private readonly ImpersonationService _impersonation;
    private readonly ILogger<UserRoleRefreshService> _logger;

    public UserRoleRefreshService(
        CustomAccountFactory accountFactory,
        OidcAuthenticationStateProvider oidcAuth,
        ImpersonationAuthStateProvider authStateProvider,
        ImpersonationService impersonation,
        ILogger<UserRoleRefreshService> logger)
    {
        _accountFactory = accountFactory;
        _oidcAuth = oidcAuth;
        _authStateProvider = authStateProvider;
        _impersonation = impersonation;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<bool> RefreshAfterSelfRoleChangeAsync()
    {
        await _impersonation.ClearAsync();
        _accountFactory.InvalidateProvisionCache();

        var cacheReset = ResetOidcUserCache(_oidcAuth.Inner, _logger);
        if (!cacheReset)
        {
            _logger.LogWarning(
                "Could not reset OIDC principal cache via reflection — caller should forceLoad.");
        }

        await _authStateProvider.NotifyPrincipalChangedAsync();
        _logger.LogInformation("Refreshed client role claims after self role change.");
        return cacheReset;
    }

    /// <summary>
    /// Forces RemoteAuthenticationService to bypass its 60s user cache on the next
    /// <c>GetAuthenticationStateAsync</c> call. Uses reflection on private fields
    /// <c>_userLastCheck</c> and <c>_cachedUser</c>.
    /// </summary>
    /// <returns><c>true</c> when both fields were found and reset.</returns>
    internal static bool ResetOidcUserCache(
        AuthenticationStateProvider inner,
        ILogger? logger = null)
    {
        var type = inner.GetType();
        var lastCheckField = type.GetField("_userLastCheck", BindingFlags.NonPublic | BindingFlags.Instance);
        var cachedUserField = type.GetField("_cachedUser", BindingFlags.NonPublic | BindingFlags.Instance);

        if (lastCheckField is null || cachedUserField is null)
        {
            logger?.LogWarning(
                "OIDC cache reset skipped: type {ProviderType} is missing expected private fields _userLastCheck / _cachedUser.",
                type.FullName);
            return false;
        }

        lastCheckField.SetValue(inner, DateTimeOffset.FromUnixTimeSeconds(0));
        cachedUserField.SetValue(inner, new ClaimsPrincipal(new ClaimsIdentity()));
        return true;
    }
}