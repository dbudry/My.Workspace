using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using My.Shared.Constants;

namespace My.Client.Services;

/// <summary>
/// Decorates the OIDC AuthenticationStateProvider so AuthorizeView and IsInRole
/// see the impersonated role set instead of the real one. Only the *global*
/// Admin role can impersonate — scoped admins (e.g. Admin:Tyme) cannot, by
/// design. The original principal stays accessible via GetRealUserAsync so
/// the toggle UI keeps working even while a lower role-set is active.
/// </summary>
public class ImpersonationAuthStateProvider : AuthenticationStateProvider, IDisposable
{
    private readonly AuthenticationStateProvider _inner;
    private readonly ImpersonationService _impersonation;

    public ImpersonationAuthStateProvider(AuthenticationStateProvider inner, ImpersonationService impersonation)
    {
        _inner = inner;
        _impersonation = impersonation;
        _inner.AuthenticationStateChanged += OnInnerChanged;
        _impersonation.Changed += OnImpersonationChanged;
    }

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        await _impersonation.InitAsync();
        var state = await _inner.GetAuthenticationStateAsync();
        var user = ApplyImpersonation(state.User);
        return ReferenceEquals(user, state.User) ? state : new AuthenticationState(user);
    }

    /// <summary>
    /// Returns the un-filtered principal — used by the impersonation toggle UI
    /// so it can detect "real Admin" status while a lower role-set is active.
    /// </summary>
    public async Task<ClaimsPrincipal> GetRealUserAsync()
    {
        var state = await _inner.GetAuthenticationStateAsync();
        return state.User;
    }

    private ClaimsPrincipal ApplyImpersonation(ClaimsPrincipal real)
    {
        if (!_impersonation.IsActive) return real;
        if (real.Identity is not { IsAuthenticated: true }) return real;
        if (!real.IsInRole(Constants.Roles.Admin)) return real; // Only global Admin can impersonate

        var source = real.Identities.FirstOrDefault();
        if (source is null) return real;

        var filtered = new ClaimsIdentity(source.AuthenticationType, source.NameClaimType, source.RoleClaimType);
        foreach (var claim in source.Claims)
        {
            if (claim.Type == ClaimTypes.Role) continue; // Drop all real roles
            filtered.AddClaim(claim);
        }
        foreach (var role in _impersonation.Roles)
        {
            filtered.AddClaim(new Claim(ClaimTypes.Role, role));
        }
        return new ClaimsPrincipal(filtered);
    }

    private void OnInnerChanged(Task<AuthenticationState> _) =>
        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());

    private void OnImpersonationChanged() =>
        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());

    /// <summary>Notifies subscribers after an out-of-band principal rebuild (e.g. role refresh).</summary>
    public Task NotifyPrincipalChangedAsync()
    {
        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
        return GetAuthenticationStateAsync();
    }

    public void Dispose()
    {
        _inner.AuthenticationStateChanged -= OnInnerChanged;
        _impersonation.Changed -= OnImpersonationChanged;
    }
}
