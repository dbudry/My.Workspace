using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using My.Shared.Constants;

namespace My.Functions.Authorization;

/// <summary>
/// Centralizes the auth-vs-permission split every gated endpoint needs to make:
/// <list type="bullet">
///   <item><b>401 Unauthorized</b> when there's no authenticated identity on the request
///   (no Bearer token, token validation failed, session truly expired).</item>
///   <item><b>403 Forbidden</b> when the identity is valid but the role check fails — the
///   caller is signed in, they just lack the role.</item>
/// </list>
/// Returning the right status matters because the SPA's UnauthorizedDelegatingHandler treats
/// 401 as a session-expiry trigger (redirect to sign-in). Returning 401 for a permission
/// failure with a still-valid Google session produces a redirect loop.
/// </summary>
public static class AuthGates
{
    /// <summary>
    /// Two-step gate for Tyme module surfaces. Returns the response the function should
    /// return, or <c>null</c> if the caller passes both checks. On success, <paramref name="userId"/>
    /// is set to the caller's DB user id and is guaranteed non-empty.
    /// </summary>
    public static IActionResult? RequireScopedTyme(
        ClaimsPrincipal principal,
        out string userId,
        string minRole = Constants.Roles.User)
        => RequireScoped(principal, Constants.Scopes.Tyme, out userId, minRole);

    /// <summary>
    /// Convenience overload for endpoints that don't need the user id from the gate (e.g. they
    /// query by some other identifier). Discards the userId.
    /// </summary>
    public static IActionResult? RequireScopedTyme(
        ClaimsPrincipal principal,
        string minRole = Constants.Roles.User)
        => RequireScopedTyme(principal, out _, minRole);

    /// <summary>
    /// Generalized variant. Most callers want <see cref="RequireScopedTyme(ClaimsPrincipal, out string, string)"/>.
    /// </summary>
    /// <summary>Authenticated caller only — no module role check.</summary>
    public static IActionResult? RequireAuthenticated(ClaimsPrincipal principal, out string userId)
    {
        userId = principal.FindFirstValue(Constants.Claims.UserId) ?? string.Empty;
        if (string.IsNullOrEmpty(userId))
            return new UnauthorizedResult();
        return null;
    }

    public static IActionResult? RequireScoped(
        ClaimsPrincipal principal,
        string scope,
        out string userId,
        string minRole = Constants.Roles.User)
    {
        // Authenticity comes from the presence of the UserId claim: AuthMiddleware is the
        // only writer, and it only adds the claim after cryptographic Bearer validation.
        // We deliberately do NOT use principal.Identity?.IsAuthenticated — the Functions
        // Worker host can pre-populate req.Identities with an anonymous identity, and
        // ClaimsPrincipal.Identity returns only the *first* identity, which would then be
        // the host's anonymous one. FindFirstValue scans every identity, so the UserId
        // claim our middleware appended is still discoverable.
        userId = principal.FindFirstValue(Constants.Claims.UserId) ?? string.Empty;
        if (string.IsNullOrEmpty(userId))
            return new UnauthorizedResult();
        if (!Constants.Roles.HasScopedAccess(principal, scope, minRole))
            return new StatusCodeResult(403);
        return null;
    }
}
