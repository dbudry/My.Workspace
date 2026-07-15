using System.Security.Claims;
using My.Shared.Constants;
using Xunit;

namespace My.Tests.Roles;

/// <summary>
/// Coverage for the role-administration helpers in <see cref="Constants.Roles"/>:
/// <see cref="Constants.Roles.IsAnyAdmin"/>,
/// <see cref="Constants.Roles.IsGlobalAdmin"/>,
/// <see cref="Constants.Roles.AdministeredScopes"/>,
/// <see cref="Constants.Roles.IsVisibleTo"/>,
/// <see cref="Constants.Roles.CanManageUser"/>,
/// <see cref="Constants.Roles.IsVisibleInTymeTeamView"/>,
/// <see cref="Constants.Roles.CanAssignRole"/>, and
/// <see cref="Constants.Roles.AssignableFor"/>.
///
/// These power the user-management UI/endpoints. Getting "which target users
/// can this admin see/edit" wrong is a privacy / authorization bug — e.g. a
/// scoped admin shouldn't be able to manage users whose roles live in a
/// different scope.
/// </summary>
public class RoleAdministrationTests
{
    private const string TymeScope = Constants.Scopes.Tyme;
    private const string UnrelatedScope = "UnrelatedScope";

    private static ClaimsPrincipal PrincipalWithRoles(params string[] roles)
    {
        var claims = roles.Select(r => new Claim(ClaimTypes.Role, r));
        var identity = new ClaimsIdentity(claims, authenticationType: "Test");
        return new ClaimsPrincipal(identity);
    }

    private static string AdminInTyme => Constants.Roles.Scoped(Constants.Roles.Admin, TymeScope);
    private static string AdminInUnrelated => Constants.Roles.Scoped(Constants.Roles.Admin, UnrelatedScope);
    private static string ManagerInTyme => Constants.Roles.Scoped(Constants.Roles.Manager, TymeScope);
    private static string UserInTyme => Constants.Roles.Scoped(Constants.Roles.User, TymeScope);

    // ---------- IsAnyAdmin ----------

    [Fact]
    public void IsAnyAdmin_true_for_global_Admin()
    {
        Assert.True(Constants.Roles.IsAnyAdmin(PrincipalWithRoles(Constants.Roles.Admin)));
    }

    [Fact]
    public void IsAnyAdmin_true_for_scoped_admin()
    {
        Assert.True(Constants.Roles.IsAnyAdmin(PrincipalWithRoles(AdminInTyme)));
    }

    [Fact]
    public void IsAnyAdmin_false_for_scoped_manager()
    {
        Assert.False(Constants.Roles.IsAnyAdmin(PrincipalWithRoles(ManagerInTyme)));
    }

    [Fact]
    public void IsAnyAdmin_false_for_no_roles()
    {
        Assert.False(Constants.Roles.IsAnyAdmin(new ClaimsPrincipal(new ClaimsIdentity())));
    }

    // ---------- IsGlobalAdmin ----------

    [Fact]
    public void IsGlobalAdmin_true_only_for_unscoped_Admin()
    {
        Assert.True(Constants.Roles.IsGlobalAdmin(PrincipalWithRoles(Constants.Roles.Admin)));
    }

    [Fact]
    public void IsGlobalAdmin_false_for_scoped_admin()
    {
        Assert.False(Constants.Roles.IsGlobalAdmin(PrincipalWithRoles(AdminInTyme)));
    }

    // ---------- AdministeredScopes ----------

    [Fact]
    public void AdministeredScopes_global_admin_returns_wildcard()
    {
        var scopes = Constants.Roles.AdministeredScopes(PrincipalWithRoles(Constants.Roles.Admin));

        Assert.Single(scopes);
        Assert.Contains(Constants.Roles.GlobalScopeWildcard, scopes);
    }

    [Fact]
    public void AdministeredScopes_scoped_admin_returns_just_that_scope()
    {
        var scopes = Constants.Roles.AdministeredScopes(PrincipalWithRoles(AdminInTyme));

        Assert.Single(scopes);
        Assert.Contains(TymeScope, scopes);
    }

    [Fact]
    public void AdministeredScopes_multiple_scoped_admins_returns_all_their_scopes()
    {
        var scopes = Constants.Roles.AdministeredScopes(PrincipalWithRoles(AdminInTyme, AdminInUnrelated));

        Assert.Equal(2, scopes.Count);
        Assert.Contains(TymeScope, scopes);
        Assert.Contains(UnrelatedScope, scopes);
    }

    [Fact]
    public void AdministeredScopes_non_admin_returns_empty()
    {
        var scopes = Constants.Roles.AdministeredScopes(PrincipalWithRoles(ManagerInTyme));

        Assert.Empty(scopes);
    }

    [Fact]
    public void AdministeredScopes_global_admin_dominates_scoped_admin()
    {
        // If you have both Admin and Admin:Tyme, the global wildcard wins —
        // returning ["*"] communicates "manages every scope, no need to enumerate".
        var scopes = Constants.Roles.AdministeredScopes(PrincipalWithRoles(Constants.Roles.Admin, AdminInTyme));

        Assert.Single(scopes);
        Assert.Contains(Constants.Roles.GlobalScopeWildcard, scopes);
    }

    // ---------- IsVisibleTo ----------

    [Fact]
    public void IsVisibleTo_global_admin_sees_every_target()
    {
        var admin = PrincipalWithRoles(Constants.Roles.Admin);

        Assert.True(Constants.Roles.IsVisibleTo(admin, new[] { Constants.Roles.Admin }));
        Assert.True(Constants.Roles.IsVisibleTo(admin, new[] { AdminInTyme }));
        Assert.True(Constants.Roles.IsVisibleTo(admin, Array.Empty<string>()));
    }

    [Fact]
    public void IsVisibleTo_scoped_admin_sees_target_with_overlapping_scoped_role()
    {
        var admin = PrincipalWithRoles(AdminInTyme);

        Assert.True(Constants.Roles.IsVisibleTo(admin, new[] { ManagerInTyme }));
        Assert.True(Constants.Roles.IsVisibleTo(admin, new[] { UserInTyme }));
    }

    [Fact]
    public void IsVisibleTo_scoped_admin_does_not_see_target_with_only_other_scope()
    {
        var admin = PrincipalWithRoles(AdminInTyme);

        Assert.False(Constants.Roles.IsVisibleTo(admin, new[] { AdminInUnrelated }));
    }

    [Fact]
    public void IsVisibleTo_scoped_admin_does_not_see_target_with_any_global_role()
    {
        // A global role on the target shields them from a scoped admin entirely —
        // global is a super-role only a global admin can reach.
        var admin = PrincipalWithRoles(AdminInTyme);

        Assert.False(Constants.Roles.IsVisibleTo(admin, new[] { Constants.Roles.Admin }));
        Assert.False(Constants.Roles.IsVisibleTo(admin, new[] { Constants.Roles.Admin, ManagerInTyme }));
    }

    [Fact]
    public void IsVisibleTo_non_admin_principal_sees_nobody()
    {
        var notAdmin = PrincipalWithRoles(ManagerInTyme);

        Assert.False(Constants.Roles.IsVisibleTo(notAdmin, new[] { UserInTyme }));
    }

    // ---------- CanManageUser ----------

    [Fact]
    public void CanManageUser_global_admin_can_manage_anyone()
    {
        var admin = PrincipalWithRoles(Constants.Roles.Admin);

        Assert.True(Constants.Roles.CanManageUser(admin, new[] { Constants.Roles.Admin }));
        Assert.True(Constants.Roles.CanManageUser(admin, new[] { AdminInUnrelated }));
    }

    [Fact]
    public void CanManageUser_scoped_admin_can_manage_if_every_target_role_is_in_scope()
    {
        var admin = PrincipalWithRoles(AdminInTyme);

        Assert.True(Constants.Roles.CanManageUser(admin, new[] { ManagerInTyme, UserInTyme }));
    }

    [Fact]
    public void CanManageUser_scoped_admin_cannot_manage_if_target_has_any_out_of_scope_role()
    {
        var admin = PrincipalWithRoles(AdminInTyme);

        Assert.False(Constants.Roles.CanManageUser(admin, new[] { ManagerInTyme, AdminInUnrelated }));
        Assert.False(Constants.Roles.CanManageUser(admin, new[] { Constants.Roles.Admin }));
    }

    // ---------- IsVisibleInTymeTeamView ----------

    [Fact]
    public void IsVisibleInTymeTeamView_ManagerInTyme_sees_Tyme_scoped_users()
    {
        var manager = PrincipalWithRoles(ManagerInTyme);

        Assert.True(Constants.Roles.IsVisibleInTymeTeamView(manager, new[] { UserInTyme }));
        Assert.True(Constants.Roles.IsVisibleInTymeTeamView(manager, new[] { ManagerInTyme }));
        Assert.False(Constants.Roles.IsVisibleInTymeTeamView(manager, new[] { Constants.Roles.User }));
    }

    [Fact]
    public void IsVisibleInTymeTeamView_AdminInTyme_uses_IsVisibleTo_rules()
    {
        var admin = PrincipalWithRoles(AdminInTyme);

        Assert.True(Constants.Roles.IsVisibleInTymeTeamView(admin, new[] { UserInTyme }));
        Assert.False(Constants.Roles.IsVisibleInTymeTeamView(admin, new[] { Constants.Roles.Admin }));
    }

    // ---------- CanAssignRole ----------

    [Fact]
    public void CanAssignRole_global_admin_can_assign_any_assignable_role()
    {
        var admin = PrincipalWithRoles(Constants.Roles.Admin);

        Assert.True(Constants.Roles.CanAssignRole(admin, Constants.Roles.Admin));
        Assert.True(Constants.Roles.CanAssignRole(admin, AdminInTyme));
        Assert.False(Constants.Roles.CanAssignRole(admin, AdminInUnrelated));
    }

    [Fact]
    public void CanAssignRole_scoped_admin_can_assign_roles_in_their_scope_only()
    {
        var admin = PrincipalWithRoles(AdminInTyme);

        Assert.True(Constants.Roles.CanAssignRole(admin, AdminInTyme));
        Assert.True(Constants.Roles.CanAssignRole(admin, ManagerInTyme));
        Assert.False(Constants.Roles.CanAssignRole(admin, AdminInUnrelated));
    }

    [Fact]
    public void CanAssignRole_scoped_admin_cannot_assign_a_global_role()
    {
        var admin = PrincipalWithRoles(AdminInTyme);

        Assert.False(Constants.Roles.CanAssignRole(admin, Constants.Roles.Admin));
    }

    [Fact]
    public void IsAssignableRole_rejects_Manager_Intranet()
    {
        var managerIntranet = Constants.Roles.Scoped(Constants.Roles.Manager, Constants.Scopes.Intranet);

        Assert.False(Constants.Roles.IsAssignableRole(managerIntranet));
        Assert.False(Constants.Roles.CanAssignRole(PrincipalWithRoles(Constants.Roles.Admin), managerIntranet));
    }

    [Fact]
    public void IsAssignableRole_rejects_Editor_Tyme()
    {
        var editorTyme = Constants.Roles.Scoped(Constants.Roles.Editor, Constants.Scopes.Tyme);

        Assert.False(Constants.Roles.IsAssignableRole(editorTyme));
        Assert.False(Constants.Roles.CanAssignRole(PrincipalWithRoles(Constants.Roles.Admin), editorTyme));
    }

    [Fact]
    public void IsAssignableRole_accepts_tyme_user_manager_admin()
    {
        Assert.True(Constants.Roles.IsAssignableRole(Constants.Roles.Scoped(Constants.Roles.User, Constants.Scopes.Tyme)));
        Assert.True(Constants.Roles.IsAssignableRole(Constants.Roles.Scoped(Constants.Roles.Manager, Constants.Scopes.Tyme)));
        Assert.True(Constants.Roles.IsAssignableRole(Constants.Roles.Scoped(Constants.Roles.Admin, Constants.Scopes.Tyme)));
    }

    [Fact]
    public void IsAssignableRole_accepts_intranet_user_editor_admin()
    {
        Assert.True(Constants.Roles.IsAssignableRole(Constants.Roles.Scoped(Constants.Roles.User, Constants.Scopes.Intranet)));
        Assert.True(Constants.Roles.IsAssignableRole(Constants.Roles.Scoped(Constants.Roles.Editor, Constants.Scopes.Intranet)));
        Assert.True(Constants.Roles.IsAssignableRole(Constants.Roles.Scoped(Constants.Roles.Admin, Constants.Scopes.Intranet)));
    }

    // ---------- AssignableFor ----------

    [Fact]
    public void AssignableFor_global_admin_returns_full_list()
    {
        var assignable = Constants.Roles.AssignableFor(PrincipalWithRoles(Constants.Roles.Admin));

        Assert.Equal(Constants.Roles.Assignable().Count, assignable.Count);
    }

    [Fact]
    public void AssignableFor_scoped_admin_returns_only_roles_in_their_scopes()
    {
        var assignable = Constants.Roles.AssignableFor(PrincipalWithRoles(AdminInTyme));

        // Every returned role must be a scoped role whose scope equals TymeScope.
        Assert.All(assignable, r =>
        {
            var colon = r.IndexOf(':');
            Assert.True(colon > 0, $"AssignableFor returned bare role '{r}' to a scoped admin.");
            Assert.Equal(TymeScope, r.Substring(colon + 1));
        });
    }

    [Fact]
    public void AssignableFor_non_admin_returns_empty()
    {
        var assignable = Constants.Roles.AssignableFor(PrincipalWithRoles(ManagerInTyme));

        Assert.Empty(assignable);
    }
}
