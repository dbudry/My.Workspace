using System.Security.Claims;
using My.Shared.Constants;
using Xunit;

namespace My.Tests.Roles;

/// <summary>
/// Exercises <see cref="Constants.Roles.HasAccess"/>. This is the function every
/// authorization gate in the app ultimately calls — getting its scoping rules
/// wrong silently grants or denies the wrong people, so it's the first thing
/// covered by tests.
///
/// Invariants under test:
///   1. Role hierarchy: User &lt; Manager &lt; Admin. A higher role satisfies a lower
///      minimum.
///   2. Global roles (no scope) grant access to *every* scope.
///   3. Scoped roles (e.g. "Admin:Tyme") only grant access to their own scope.
///   4. A user with no relevant role is denied.
/// </summary>
public class RoleAccessTests
{
    private const string TymeScope = Constants.Scopes.Tyme;

    // A scope name that isn't in Constants.Scopes — used as a foil to prove
    // scoped roles don't leak across scopes. The value is deliberately
    // self-describing so a reader doesn't mistake it for a real future scope.
    private const string UnrelatedScope = "UnrelatedScope";

    private static ClaimsPrincipal PrincipalWithRoles(params string[] roles)
    {
        var claims = roles.Select(r => new Claim(ClaimTypes.Role, r));
        var identity = new ClaimsIdentity(claims, authenticationType: "Test");
        return new ClaimsPrincipal(identity);
    }

    // ---------- Global roles grant every scope ----------

    [Fact]
    public void GlobalAdmin_can_access_any_scope_at_any_level()
    {
        var principal = PrincipalWithRoles(Constants.Roles.Admin);

        Assert.True(Constants.Roles.HasAccess(principal, TymeScope, Constants.Roles.User));
        Assert.True(Constants.Roles.HasAccess(principal, TymeScope, Constants.Roles.Manager));
        Assert.True(Constants.Roles.HasAccess(principal, TymeScope, Constants.Roles.Admin));
        Assert.True(Constants.Roles.HasAccess(principal, UnrelatedScope, Constants.Roles.Admin));
    }

    // Note: global Manager and global User aren't assignable roles in this system
    // (see Constants.Roles.Assignable — only Admin is global; Manager/User come
    // only in scoped form). So there are no global-Manager / global-User tests.

    // ---------- Scoped roles only grant their own scope ----------

    [Fact]
    public void AdminInTyme_can_access_Tyme_only()
    {
        var principal = PrincipalWithRoles(Constants.Roles.Scoped(Constants.Roles.Admin, TymeScope));

        Assert.True(Constants.Roles.HasAccess(principal, TymeScope, Constants.Roles.Admin));
        Assert.False(Constants.Roles.HasAccess(principal, UnrelatedScope, Constants.Roles.Admin));
        Assert.False(Constants.Roles.HasAccess(principal, UnrelatedScope, Constants.Roles.User));
    }

    [Fact]
    public void ManagerInTyme_can_access_Manager_and_below_in_Tyme()
    {
        var principal = PrincipalWithRoles(Constants.Roles.Scoped(Constants.Roles.Manager, TymeScope));

        Assert.True(Constants.Roles.HasAccess(principal, TymeScope, Constants.Roles.User));
        Assert.True(Constants.Roles.HasAccess(principal, TymeScope, Constants.Roles.Manager));
        Assert.False(Constants.Roles.HasAccess(principal, TymeScope, Constants.Roles.Admin));
    }

    [Fact]
    public void UserInTyme_can_access_only_User_in_Tyme()
    {
        var principal = PrincipalWithRoles(Constants.Roles.Scoped(Constants.Roles.User, TymeScope));

        Assert.True(Constants.Roles.HasAccess(principal, TymeScope, Constants.Roles.User));
        Assert.False(Constants.Roles.HasAccess(principal, TymeScope, Constants.Roles.Manager));
        Assert.False(Constants.Roles.HasAccess(principal, UnrelatedScope, Constants.Roles.User));
    }

    // ---------- Role hierarchy ordering ----------

    [Fact]
    public void Admin_satisfies_Manager_minimum()
    {
        var principal = PrincipalWithRoles(Constants.Roles.Scoped(Constants.Roles.Admin, TymeScope));

        Assert.True(Constants.Roles.HasAccess(principal, TymeScope, Constants.Roles.Manager));
    }

    [Fact]
    public void Manager_does_not_satisfy_Admin_minimum()
    {
        var principal = PrincipalWithRoles(Constants.Roles.Scoped(Constants.Roles.Manager, TymeScope));

        Assert.False(Constants.Roles.HasAccess(principal, TymeScope, Constants.Roles.Admin));
    }

    // ---------- Unrelated and empty principals ----------

    [Fact]
    public void No_roles_is_denied()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity());

        Assert.False(Constants.Roles.HasAccess(principal, TymeScope, Constants.Roles.User));
    }

    [Fact]
    public void Role_in_other_scope_is_denied()
    {
        var principal = PrincipalWithRoles(Constants.Roles.Scoped(Constants.Roles.Admin, UnrelatedScope));

        Assert.False(Constants.Roles.HasAccess(principal, TymeScope, Constants.Roles.User));
        Assert.True(Constants.Roles.HasAccess(principal, UnrelatedScope, Constants.Roles.Admin));
    }

    // ---------- Default minimum is User ----------

    [Fact]
    public void Default_minimum_role_is_User()
    {
        var principal = PrincipalWithRoles(Constants.Roles.Scoped(Constants.Roles.User, TymeScope));

        Assert.True(Constants.Roles.HasAccess(principal, TymeScope));
    }

    // ---------- Scoped() formatting ----------

    [Theory]
    [InlineData(Constants.Roles.Admin, "Tyme", "Admin:Tyme")]
    [InlineData(Constants.Roles.Manager, "Tyme", "Manager:Tyme")]
    [InlineData(Constants.Roles.User, "Tyme", "User:Tyme")]
    [InlineData(Constants.Roles.Admin, null, Constants.Roles.Admin)]
    [InlineData(Constants.Roles.Admin, "", Constants.Roles.Admin)]
    public void Scoped_formats_as_RoleColonScope(string role, string? scope, string expected)
    {
        Assert.Equal(expected, Constants.Roles.Scoped(role, scope));
    }

    // ---------- HasScopedAccess: strict scoped variant ----------
    //
    // HasScopedAccess ignores global roles — only a scoped role *inside* the named
    // scope satisfies the check. Used for module-specific surfaces (e.g. the Submit
    // page's "Team submissions" / unsubmit endpoint) where global Admin is not the
    // intended audience.

    [Fact]
    public void HasScopedAccess_global_Admin_does_NOT_satisfy_scoped_gate()
    {
        var principal = PrincipalWithRoles(Constants.Roles.Admin);

        Assert.False(Constants.Roles.HasScopedAccess(principal, TymeScope, Constants.Roles.User));
        Assert.False(Constants.Roles.HasScopedAccess(principal, TymeScope, Constants.Roles.Manager));
        Assert.False(Constants.Roles.HasScopedAccess(principal, TymeScope, Constants.Roles.Admin));
    }

    [Fact]
    public void HasScopedAccess_ManagerInTyme_satisfies_Manager_in_Tyme()
    {
        var principal = PrincipalWithRoles(Constants.Roles.Scoped(Constants.Roles.Manager, TymeScope));

        Assert.True(Constants.Roles.HasScopedAccess(principal, TymeScope, Constants.Roles.User));
        Assert.True(Constants.Roles.HasScopedAccess(principal, TymeScope, Constants.Roles.Manager));
        Assert.False(Constants.Roles.HasScopedAccess(principal, TymeScope, Constants.Roles.Admin));
    }

    [Fact]
    public void HasScopedAccess_AdminInTyme_satisfies_Manager_in_Tyme()
    {
        var principal = PrincipalWithRoles(Constants.Roles.Scoped(Constants.Roles.Admin, TymeScope));

        Assert.True(Constants.Roles.HasScopedAccess(principal, TymeScope, Constants.Roles.Manager));
        Assert.True(Constants.Roles.HasScopedAccess(principal, TymeScope, Constants.Roles.Admin));
    }

    [Fact]
    public void HasScopedAccess_scoped_role_does_not_leak_across_scopes()
    {
        var principal = PrincipalWithRoles(Constants.Roles.Scoped(Constants.Roles.Admin, UnrelatedScope));

        Assert.False(Constants.Roles.HasScopedAccess(principal, TymeScope, Constants.Roles.User));
        Assert.True(Constants.Roles.HasScopedAccess(principal, UnrelatedScope, Constants.Roles.Admin));
    }

    [Fact]
    public void HasScopedAccess_empty_scope_is_denied()
    {
        var principal = PrincipalWithRoles(Constants.Roles.Admin);

        Assert.False(Constants.Roles.HasScopedAccess(principal, "", Constants.Roles.User));
    }

    [Fact]
    public void HasScopedAccess_no_roles_is_denied()
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity());

        Assert.False(Constants.Roles.HasScopedAccess(principal, TymeScope, Constants.Roles.User));
    }
}
