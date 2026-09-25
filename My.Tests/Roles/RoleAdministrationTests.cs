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
/// <see cref="Constants.Roles.CanChangeUserActiveStatus"/>,
/// <see cref="Constants.Roles.IsVisibleInTymeTeamView"/>,
/// <see cref="Constants.Roles.CanViewTymeTeamReports"/>,
/// <see cref="Constants.Roles.CanAssignRole"/>,
/// <see cref="Constants.Roles.AssignableFor"/>, and
/// <see cref="Constants.Roles.TryMergeRoleUpdate"/>.
///
/// These power the user-management UI/endpoints. Any admin can see every
/// account on Users. Role updates merge in-scope changes and leave other
/// modules alone. Account-level mutations still use
/// <see cref="Constants.Roles.CanManageUser"/>. Tyme team surfaces stay Tyme-scoped.
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

    private static string UserAccessInTyme => Constants.Roles.Scoped(Constants.Roles.UserAccess, TymeScope);
    private static string UserAccessInUnrelated => Constants.Roles.Scoped(Constants.Roles.UserAccess, UnrelatedScope);
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
        Assert.True(Constants.Roles.IsAnyAdmin(PrincipalWithRoles(UserAccessInTyme)));
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
        Assert.False(Constants.Roles.IsGlobalAdmin(PrincipalWithRoles(UserAccessInTyme)));
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
        var scopes = Constants.Roles.AdministeredScopes(PrincipalWithRoles(UserAccessInTyme));

        Assert.Single(scopes);
        Assert.Contains(TymeScope, scopes);
    }

    [Fact]
    public void AdministeredScopes_multiple_scoped_admins_returns_all_their_scopes()
    {
        var scopes = Constants.Roles.AdministeredScopes(PrincipalWithRoles(UserAccessInTyme, UserAccessInUnrelated));

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
        var scopes = Constants.Roles.AdministeredScopes(PrincipalWithRoles(Constants.Roles.Admin, UserAccessInTyme));

        Assert.Single(scopes);
        Assert.Contains(Constants.Roles.GlobalScopeWildcard, scopes);
    }

    // ---------- IsVisibleTo ----------

    [Fact]
    public void IsVisibleTo_global_admin_sees_every_target()
    {
        var admin = PrincipalWithRoles(Constants.Roles.Admin);

        Assert.True(Constants.Roles.IsVisibleTo(admin, new[] { Constants.Roles.Admin }));
        Assert.True(Constants.Roles.IsVisibleTo(admin, new[] { UserAccessInTyme }));
        Assert.True(Constants.Roles.IsVisibleTo(admin, Array.Empty<string>()));
    }

    [Fact]
    public void IsVisibleTo_scoped_admin_sees_target_with_overlapping_scoped_role()
    {
        var admin = PrincipalWithRoles(UserAccessInTyme);

        Assert.True(Constants.Roles.IsVisibleTo(admin, new[] { ManagerInTyme }));
        Assert.True(Constants.Roles.IsVisibleTo(admin, new[] { UserInTyme }));
    }

    [Fact]
    public void IsVisibleTo_scoped_admin_sees_target_with_only_other_scope()
    {
        var admin = PrincipalWithRoles(UserAccessInTyme);

        Assert.True(Constants.Roles.IsVisibleTo(admin, new[] { UserAccessInUnrelated }));
    }

    [Fact]
    public void IsVisibleTo_scoped_admin_sees_target_with_any_global_role()
    {
        // Directory visibility is open so a scoped admin can find anyone.
        // Role updates merge in-scope changes; a global role on the target stays.
        var admin = PrincipalWithRoles(UserAccessInTyme);

        Assert.True(Constants.Roles.IsVisibleTo(admin, new[] { Constants.Roles.Admin }));
        Assert.True(Constants.Roles.IsVisibleTo(admin, new[] { Constants.Roles.Admin, ManagerInTyme }));
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
        Assert.True(Constants.Roles.CanManageUser(admin, new[] { UserAccessInUnrelated }));
    }

    [Fact]
    public void CanManageUser_scoped_admin_can_manage_if_every_target_role_is_in_scope()
    {
        var admin = PrincipalWithRoles(UserAccessInTyme);

        Assert.True(Constants.Roles.CanManageUser(admin, new[] { ManagerInTyme, UserInTyme }));
    }

    [Fact]
    public void CanManageUser_scoped_admin_cannot_manage_if_target_has_any_out_of_scope_role()
    {
        var admin = PrincipalWithRoles(UserAccessInTyme);

        Assert.False(Constants.Roles.CanManageUser(admin, new[] { ManagerInTyme, UserAccessInUnrelated }));
        Assert.False(Constants.Roles.CanManageUser(admin, new[] { Constants.Roles.Admin }));
    }

    [Fact]
    public void CanChangeUserActiveStatus_true_only_for_global_Admin()
    {
        Assert.True(Constants.Roles.CanChangeUserActiveStatus(PrincipalWithRoles(Constants.Roles.Admin)));
        Assert.False(Constants.Roles.CanChangeUserActiveStatus(PrincipalWithRoles(UserAccessInTyme)));
        Assert.False(Constants.Roles.CanChangeUserActiveStatus(PrincipalWithRoles(ManagerInTyme)));
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
    public void IsVisibleInTymeTeamView_UserAccessInTyme_sees_Tyme_scoped_users_only()
    {
        var admin = PrincipalWithRoles(UserAccessInTyme);

        Assert.True(Constants.Roles.IsVisibleInTymeTeamView(admin, new[] { UserInTyme }));
        Assert.False(Constants.Roles.IsVisibleInTymeTeamView(admin, new[] { Constants.Roles.Admin }));
        Assert.False(Constants.Roles.IsVisibleInTymeTeamView(admin, new[] { UserAccessInUnrelated }));
    }

    // ---------- CanViewTymeTeamReports ----------

    [Fact]
    public void CanViewTymeTeamReports_ManagerInTyme_always()
    {
        var manager = PrincipalWithRoles(ManagerInTyme);

        Assert.True(Constants.Roles.CanViewTymeTeamReports(manager, allowUsers: false));
        Assert.True(Constants.Roles.CanViewTymeTeamReports(manager, allowUsers: true));
    }

    [Fact]
    public void CanViewTymeTeamReports_UserAccessInTyme_does_not()
    {
        var access = PrincipalWithRoles(UserAccessInTyme);

        Assert.False(Constants.Roles.CanViewTymeTeamReports(access, allowUsers: false));
        Assert.False(Constants.Roles.CanViewTymeTeamReports(access, allowUsers: true));
    }

    [Fact]
    public void IsVisibleInTymeTeamView_UserAccess_target_is_not_a_tyme_operator()
    {
        var manager = PrincipalWithRoles(ManagerInTyme);

        Assert.False(Constants.Roles.IsVisibleInTymeTeamView(manager, new[] { UserAccessInTyme }));
    }

    [Fact]
    public void CanViewTymeTeamReports_UserInTyme_only_when_workspace_allows()
    {
        var user = PrincipalWithRoles(UserInTyme);

        Assert.False(Constants.Roles.CanViewTymeTeamReports(user, allowUsers: false));
        Assert.True(Constants.Roles.CanViewTymeTeamReports(user, allowUsers: true));
    }

    [Fact]
    public void CanViewTymeTeamReports_EditorInTyme_only_when_workspace_allows()
    {
        var editor = PrincipalWithRoles(Constants.Roles.Scoped(Constants.Roles.Editor, TymeScope));

        Assert.False(Constants.Roles.CanViewTymeTeamReports(editor, allowUsers: false));
        Assert.True(Constants.Roles.CanViewTymeTeamReports(editor, allowUsers: true));
    }

    [Fact]
    public void CanViewTymeTeamReports_global_Admin_alone_does_not()
    {
        var global = PrincipalWithRoles(Constants.Roles.Admin);

        Assert.False(Constants.Roles.CanViewTymeTeamReports(global, allowUsers: true));
    }

    // ---------- CanAssignRole ----------

    [Fact]
    public void CanAssignRole_global_admin_can_assign_any_assignable_role()
    {
        var admin = PrincipalWithRoles(Constants.Roles.Admin);

        Assert.True(Constants.Roles.CanAssignRole(admin, Constants.Roles.Admin));
        Assert.True(Constants.Roles.CanAssignRole(admin, UserAccessInTyme));
        Assert.False(Constants.Roles.CanAssignRole(admin, UserAccessInUnrelated));
    }

    [Fact]
    public void CanAssignRole_scoped_admin_can_assign_roles_in_their_scope_only()
    {
        var admin = PrincipalWithRoles(UserAccessInTyme);

        Assert.False(Constants.Roles.CanAssignRole(admin, UserAccessInTyme));
        Assert.True(Constants.Roles.CanAssignRole(admin, ManagerInTyme));
        Assert.False(Constants.Roles.CanAssignRole(admin, UserAccessInUnrelated));
    }

    [Fact]
    public void CanAssignRole_scoped_admin_cannot_assign_a_global_role()
    {
        var admin = PrincipalWithRoles(UserAccessInTyme);

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
    public void IsAssignableRole_accepts_Editor_Tyme()
    {
        // Editor:Tyme grants project create/edit only (see ProjectFunction Create/UpdateProject
        // and ProjectManager.razor's canEditProjects) — no delete/archive/group management or
        // team surfaces, so it's now offered alongside Manager:Tyme / Admin:Tyme.
        var editorTyme = Constants.Roles.Scoped(Constants.Roles.Editor, Constants.Scopes.Tyme);

        Assert.True(Constants.Roles.IsAssignableRole(editorTyme));
        Assert.True(Constants.Roles.CanAssignRole(PrincipalWithRoles(Constants.Roles.Admin), editorTyme));
    }

    [Fact]
    public void IsAssignableRole_accepts_tyme_user_editor_manager_useraccess()
    {
        Assert.True(Constants.Roles.IsAssignableRole(Constants.Roles.Scoped(Constants.Roles.User, Constants.Scopes.Tyme)));
        Assert.True(Constants.Roles.IsAssignableRole(Constants.Roles.Scoped(Constants.Roles.Editor, Constants.Scopes.Tyme)));
        Assert.True(Constants.Roles.IsAssignableRole(Constants.Roles.Scoped(Constants.Roles.Manager, Constants.Scopes.Tyme)));
        Assert.True(Constants.Roles.IsAssignableRole(UserAccessInTyme));
        Assert.False(Constants.Roles.IsAssignableRole(Constants.Roles.Scoped(Constants.Roles.Admin, Constants.Scopes.Tyme)));
    }

    [Fact]
    public void IsAssignableRole_accepts_intranet_user_editor_navigation_useraccess()
    {
        Assert.True(Constants.Roles.IsAssignableRole(Constants.Roles.Scoped(Constants.Roles.User, Constants.Scopes.Intranet)));
        Assert.True(Constants.Roles.IsAssignableRole(Constants.Roles.Scoped(Constants.Roles.Editor, Constants.Scopes.Intranet)));
        Assert.True(Constants.Roles.IsAssignableRole(Constants.Roles.Scoped(Constants.Roles.Navigation, Constants.Scopes.Intranet)));
        Assert.True(Constants.Roles.IsAssignableRole(Constants.Roles.Scoped(Constants.Roles.UserAccess, Constants.Scopes.Intranet)));
        Assert.False(Constants.Roles.IsAssignableRole(Constants.Roles.Scoped(Constants.Roles.Admin, Constants.Scopes.Intranet)));
    }

    [Fact]
    public void IsAssignableRole_accepts_organizations_user_editor_maintenance_useraccess()
    {
        Assert.True(Constants.Roles.IsAssignableRole(Constants.Roles.Scoped(Constants.Roles.User, Constants.Scopes.Organizations)));
        Assert.True(Constants.Roles.IsAssignableRole(Constants.Roles.Scoped(Constants.Roles.Editor, Constants.Scopes.Organizations)));
        Assert.True(Constants.Roles.IsAssignableRole(Constants.Roles.Scoped(Constants.Roles.Manager, Constants.Scopes.Organizations)));
        Assert.True(Constants.Roles.IsAssignableRole(Constants.Roles.Scoped(Constants.Roles.UserAccess, Constants.Scopes.Organizations)));
        Assert.False(Constants.Roles.IsAssignableRole(Constants.Roles.Scoped(Constants.Roles.Admin, Constants.Scopes.Organizations)));
    }

    [Fact]
    public void IsAssignableRole_accepts_crm_user_editor_manager_useraccess()
    {
        Assert.True(Constants.Roles.IsAssignableRole(Constants.Roles.Scoped(Constants.Roles.User, Constants.Scopes.Crm)));
        Assert.True(Constants.Roles.IsAssignableRole(Constants.Roles.Scoped(Constants.Roles.Editor, Constants.Scopes.Crm)));
        Assert.True(Constants.Roles.IsAssignableRole(Constants.Roles.Scoped(Constants.Roles.Manager, Constants.Scopes.Crm)));
        Assert.True(Constants.Roles.IsAssignableRole(Constants.Roles.Scoped(Constants.Roles.UserAccess, Constants.Scopes.Crm)));
        Assert.False(Constants.Roles.IsAssignableRole(Constants.Roles.Scoped(Constants.Roles.Admin, Constants.Scopes.Crm)));
    }

    [Fact]
    public void IsAssignableRole_accepts_expenses_user_manager_useraccess()
    {
        Assert.True(Constants.Roles.IsAssignableRole(Constants.Roles.Scoped(Constants.Roles.User, Constants.Scopes.Expenses)));
        Assert.True(Constants.Roles.IsAssignableRole(Constants.Roles.Scoped(Constants.Roles.Manager, Constants.Scopes.Expenses)));
        Assert.True(Constants.Roles.IsAssignableRole(Constants.Roles.Scoped(Constants.Roles.UserAccess, Constants.Scopes.Expenses)));
        Assert.False(Constants.Roles.IsAssignableRole(Constants.Roles.Scoped(Constants.Roles.Admin, Constants.Scopes.Expenses)));
        Assert.False(Constants.Roles.IsAssignableRole(Constants.Roles.Scoped(Constants.Roles.Editor, Constants.Scopes.Expenses)));
    }

    [Fact]
    public void CanAssignRole_user_access_cannot_grant_user_access()
    {
        var access = PrincipalWithRoles(UserAccessInTyme);
        Assert.True(Constants.Roles.CanAssignRole(access, ManagerInTyme));
        Assert.False(Constants.Roles.CanAssignRole(access, UserAccessInTyme));
        Assert.True(Constants.Roles.CanAssignRole(PrincipalWithRoles(Constants.Roles.Admin), UserAccessInTyme));
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
        var assignable = Constants.Roles.AssignableFor(PrincipalWithRoles(UserAccessInTyme));

        // Every returned role must be a scoped role whose scope equals TymeScope.
        Assert.All(assignable, r =>
        {
            var colon = r.IndexOf(':');
            Assert.True(colon > 0, $"AssignableFor returned bare role '{r}' to a scoped admin.");
            Assert.Equal(TymeScope, r.Substring(colon + 1));
        });
    }

    [Fact]
    public void AssignableFor_organizations_admin_returns_only_organizations_roles()
    {
        var assignable = Constants.Roles.AssignableFor(
            PrincipalWithRoles(Constants.Roles.Scoped(Constants.Roles.UserAccess, Constants.Scopes.Organizations)));

        Assert.All(assignable, r =>
        {
            var colon = r.IndexOf(':');
            Assert.True(colon > 0, $"AssignableFor returned bare role '{r}' to Organizations User Access.");
            Assert.Equal(Constants.Scopes.Organizations, r.Substring(colon + 1));
        });
        Assert.Contains(Constants.Roles.Scoped(Constants.Roles.Manager, Constants.Scopes.Organizations), assignable);
        Assert.Contains(Constants.Roles.Scoped(Constants.Roles.Editor, Constants.Scopes.Organizations), assignable);
        Assert.Contains(Constants.Roles.Scoped(Constants.Roles.User, Constants.Scopes.Organizations), assignable);
        Assert.DoesNotContain(Constants.Roles.Scoped(Constants.Roles.UserAccess, Constants.Scopes.Organizations), assignable);
    }

    [Fact]
    public void AssignableFor_non_admin_returns_empty()
    {
        var assignable = Constants.Roles.AssignableFor(PrincipalWithRoles(ManagerInTyme));

        Assert.Empty(assignable);
    }

    // ---------- TryMergeRoleUpdate ----------

    private static string EditorInOrganizations =>
        Constants.Roles.Scoped(Constants.Roles.Editor, Constants.Scopes.Organizations);

    private static string UserInIntranet =>
        Constants.Roles.Scoped(Constants.Roles.User, Constants.Scopes.Intranet);

    [Fact]
    public void TryMergeRoleUpdate_scoped_admin_adds_tyme_preserving_other_modules()
    {
        var admin = PrincipalWithRoles(UserAccessInTyme);
        var current = new[] { EditorInOrganizations, UserInIntranet };
        var requested = new[] { ManagerInTyme };

        Assert.True(Constants.Roles.TryMergeRoleUpdate(admin, current, requested, out var merged));
        Assert.Equal(3, merged.Count);
        Assert.Contains(ManagerInTyme, merged);
        Assert.Contains(EditorInOrganizations, merged);
        Assert.Contains(UserInIntranet, merged);
    }

    [Fact]
    public void TryMergeRoleUpdate_scoped_admin_cannot_request_out_of_scope_role()
    {
        var admin = PrincipalWithRoles(UserAccessInTyme);

        Assert.False(Constants.Roles.TryMergeRoleUpdate(
            admin,
            new[] { UserInTyme },
            new[] { UserInTyme, EditorInOrganizations },
            out _));
    }

    [Fact]
    public void TryMergeRoleUpdate_scoped_admin_can_remove_in_scope_role()
    {
        var admin = PrincipalWithRoles(UserAccessInTyme);

        Assert.True(Constants.Roles.TryMergeRoleUpdate(
            admin,
            new[] { UserInTyme, EditorInOrganizations },
            Array.Empty<string>(),
            out var merged));
        Assert.Single(merged);
        Assert.Contains(EditorInOrganizations, merged);
    }

    [Fact]
    public void TryMergeRoleUpdate_global_admin_drops_unknown_legacy()
    {
        var admin = PrincipalWithRoles(Constants.Roles.Admin);

        Assert.True(Constants.Roles.TryMergeRoleUpdate(
            admin,
            new[] { UserInTyme, "ObsoleteRole" },
            new[] { UserInTyme },
            out var merged));
        Assert.Single(merged);
        Assert.Contains(UserInTyme, merged);
    }

    [Fact]
    public void TryMergeRoleUpdate_scoped_admin_preserves_unknown_legacy()
    {
        var admin = PrincipalWithRoles(UserAccessInTyme);

        Assert.True(Constants.Roles.TryMergeRoleUpdate(
            admin,
            new[] { UserInTyme, "ObsoleteRole" },
            new[] { UserInTyme },
            out var merged));
        Assert.Equal(2, merged.Count);
        Assert.Contains(UserInTyme, merged);
        Assert.Contains("ObsoleteRole", merged);
    }

    [Fact]
    public void TryMergeRoleUpdate_rejects_empty_merged()
    {
        var admin = PrincipalWithRoles(UserAccessInTyme);

        Assert.False(Constants.Roles.TryMergeRoleUpdate(
            admin,
            new[] { UserInTyme },
            Array.Empty<string>(),
            out _));
    }

    [Fact]
    public void TryMergeRoleUpdate_non_admin_returns_false()
    {
        var manager = PrincipalWithRoles(ManagerInTyme);

        Assert.False(Constants.Roles.TryMergeRoleUpdate(
            manager,
            new[] { UserInTyme },
            new[] { UserInTyme },
            out _));
    }
}
