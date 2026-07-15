using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using My.Functions.Authorization;
using My.Shared.Constants;
using Xunit;

namespace My.Tests.Authorization;

/// <summary>
/// Exercises <see cref="AuthGates"/> — the helper that maps "no auth / no permission /
/// passed" into the right HTTP status codes. Returning the wrong code is a real bug:
/// returning 401 for a permission failure with a still-valid client session is what
/// caused the auth redirect loop fixed in this PR.
///
/// Invariants under test:
/// <list type="bullet">
///   <item>No authenticated identity → 401, regardless of role check.</item>
///   <item>Authenticated identity but missing scoped role → 403.</item>
///   <item>Authenticated identity with a scoped role at the required level → null (pass).</item>
///   <item>Global Admin alone does not satisfy a Tyme-scoped gate.</item>
///   <item>The out userId param is empty on failure and the actual user id on success.</item>
/// </list>
/// </summary>
public class AuthGatesTests
{
    private const string UserIdValue = "user-123";

    private static ClaimsPrincipal Anonymous() => new(new ClaimsIdentity());

    private static ClaimsPrincipal Authenticated(params string[] roles)
    {
        var claims = new List<Claim>
        {
            new(Constants.Claims.UserId, UserIdValue),
        };
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));
        // AuthenticationType is what flips Identity.IsAuthenticated to true.
        var identity = new ClaimsIdentity(claims, authenticationType: "Test");
        return new ClaimsPrincipal(identity);
    }

    [Fact]
    public void Anonymous_caller_gets_401()
    {
        var result = AuthGates.RequireScopedTyme(Anonymous(), out var userId);

        Assert.IsType<UnauthorizedResult>(result);
        Assert.Equal(string.Empty, userId);
    }

    [Fact]
    public void Authenticated_without_scoped_role_gets_403()
    {
        // Authenticated identity (valid Bearer token), but no Tyme-scoped role.
        // This is the global-Admin-without-scoped-role case that produced the loop.
        var principal = Authenticated(Constants.Roles.Admin);

        var result = AuthGates.RequireScopedTyme(principal, out var userId);

        var status = Assert.IsType<StatusCodeResult>(result);
        Assert.Equal(403, status.StatusCode);
        // userId is filled even on permission failure — only auth failure blanks it.
        Assert.Equal(UserIdValue, userId);
    }

    [Fact]
    public void Authenticated_with_user_role_passes_user_gate()
    {
        var principal = Authenticated(Constants.Roles.Scoped(Constants.Roles.User, Constants.Scopes.Tyme));

        var result = AuthGates.RequireScopedTyme(principal, out var userId);

        Assert.Null(result);
        Assert.Equal(UserIdValue, userId);
    }

    [Fact]
    public void Authenticated_with_user_role_fails_manager_gate_with_403()
    {
        var principal = Authenticated(Constants.Roles.Scoped(Constants.Roles.User, Constants.Scopes.Tyme));

        var result = AuthGates.RequireScopedTyme(principal, out _, Constants.Roles.Manager);

        var status = Assert.IsType<StatusCodeResult>(result);
        Assert.Equal(403, status.StatusCode);
    }

    [Fact]
    public void Authenticated_with_manager_role_passes_manager_gate()
    {
        var principal = Authenticated(Constants.Roles.Scoped(Constants.Roles.Manager, Constants.Scopes.Tyme));

        var result = AuthGates.RequireScopedTyme(principal, out _, Constants.Roles.Manager);

        Assert.Null(result);
    }

    [Fact]
    public void Global_admin_alone_does_NOT_satisfy_a_scoped_gate()
    {
        // The bug. Pre-fix, this returned 401 (because HasAccess granted, but the gate
        // composition was wrong). Post-fix, RequireScopedTyme returns 403 because the
        // role check is HasScopedAccess and global Admin doesn't count as a scoped role.
        var principal = Authenticated(Constants.Roles.Admin);

        var result = AuthGates.RequireScopedTyme(principal, out _, Constants.Roles.Manager);

        var status = Assert.IsType<StatusCodeResult>(result);
        Assert.Equal(403, status.StatusCode);
    }

    [Fact]
    public void Discard_overload_is_equivalent_to_out_var()
    {
        var principal = Authenticated(Constants.Roles.Scoped(Constants.Roles.User, Constants.Scopes.Tyme));

        // The no-out-param overload should agree with the out-param one.
        Assert.Null(AuthGates.RequireScopedTyme(principal));
        Assert.Null(AuthGates.RequireScopedTyme(principal, out _));
    }

    [Fact]
    public void Authenticated_with_no_userId_claim_gets_401()
    {
        // Edge case: auth middleware ran (identity is authenticated) but didn't write the
        // app's internal user id claim. Treat as not-authenticated rather than 403, since
        // we can't safely act on a request with no actor id.
        var identity = new ClaimsIdentity(Array.Empty<Claim>(), authenticationType: "Test");
        var principal = new ClaimsPrincipal(identity);

        var result = AuthGates.RequireScopedTyme(principal, out var userId);

        Assert.IsType<UnauthorizedResult>(result);
        Assert.Equal(string.Empty, userId);
    }

    [Fact]
    public void Anonymous_first_identity_does_not_mask_authenticated_second_identity()
    {
        // Regression: the Functions Worker host can populate req.Identities with an
        // anonymous identity before AuthMiddleware appends its GoogleOIDC identity. The
        // gate used to check principal.Identity.IsAuthenticated, which returns the *first*
        // identity only and so reported "anonymous" — every Tyme endpoint then 401'd while
        // ProvisionAsync (which checks claim presence, not Identity.IsAuthenticated)
        // succeeded. Trust the UserId claim, not the first identity's auth flag.
        var anonymous = new ClaimsIdentity(); // no auth type → IsAuthenticated == false
        var ours = new ClaimsIdentity(
            new[]
            {
                new Claim(Constants.Claims.UserId, UserIdValue),
                new Claim(ClaimTypes.Role, Constants.Roles.Scoped(Constants.Roles.User, Constants.Scopes.Tyme))
            },
            authenticationType: "GoogleOIDC");
        var principal = new ClaimsPrincipal(new[] { anonymous, ours });

        var result = AuthGates.RequireScopedTyme(principal, out var userId);

        Assert.Null(result);
        Assert.Equal(UserIdValue, userId);
    }
}
