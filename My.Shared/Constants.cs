namespace My.Shared.Constants
{
    public static class Constants
    {
        public static class Roles
        {
            public const string Admin = "Admin";
            public const string Manager = "Manager";
            public const string Editor = "Editor";
            public const string User = "User";
            /// <summary>Orthogonal: assign operational roles in a scope. Does not operate the module.</summary>
            public const string UserAccess = "UserAccess";
            /// <summary>Orthogonal: Intranet curated sidebar. Does not stack over Editor.</summary>
            public const string Navigation = "Navigation";

            /// <summary>
            /// Sentinel value used in <see cref="AdministeredScopes"/> to mean
            /// "global authority — manages every scope".
            /// </summary>
            public const string GlobalScopeWildcard = "*";

            /// <summary>Day-to-day module ladder. Admin is global-only and is not stacked here.</summary>
            public static readonly string[] OperationalHierarchy = [User, Editor, Manager];

            /// <summary>Exact-match roles — they do not satisfy User/Editor/Manager gates.</summary>
            public static readonly string[] OrthogonalRoles = [UserAccess, Navigation];

            /// <summary>
            /// Formats a scoped role, e.g. "Manager:Tyme". Pass null scope for a global role.
            /// </summary>
            public static string Scoped(string role, string? scope) =>
                string.IsNullOrEmpty(scope) ? role : $"{role}:{scope}";

            public static bool IsOperationalRole(string? roleName) =>
                roleName is User or Editor or Manager;

            public static bool IsOrthogonalRole(string? roleName) =>
                roleName is UserAccess or Navigation;

            /// <summary>Picker/chip label: "Expenses — User Access", "Global — Admin".</summary>
            public static string FormatRole(string? role)
            {
                if (string.IsNullOrEmpty(role)) return string.Empty;
                var i = role.IndexOf(':');
                var roleName = i < 0 ? role : role[..i];
                var scope = i < 0 ? string.Empty : role[(i + 1)..];
                if (roleName == UserAccess) roleName = "User Access";
                return string.IsNullOrEmpty(scope) ? $"Global — {roleName}" : $"{scope} — {roleName}";
            }

            /// <summary>
            /// Permissive scope check: global Admin satisfies operational minimums.
            /// Module surfaces should use <see cref="HasScopedAccess"/>.
            /// </summary>
            public static bool HasAccess(System.Security.Claims.ClaimsPrincipal principal, string scope, string minimumRole = User)
            {
                if (IsOrthogonalRole(minimumRole))
                    return principal.IsInRole(Scoped(minimumRole, scope));

                if (IsGlobalAdmin(principal) && IsOperationalRole(minimumRole))
                    return true;

                var requiredLevel = Array.IndexOf(OperationalHierarchy, minimumRole);
                if (requiredLevel < 0) return false;

                foreach (var role in OperationalHierarchy.Skip(requiredLevel))
                {
                    if (principal.IsInRole(role))
                        return true;
                    if (principal.IsInRole(Scoped(role, scope)))
                        return true;
                }

                return false;
            }

            /// <summary>
            /// Default gate for module surfaces: only a scoped role inside <paramref name="scope"/>
            /// satisfies the check; global roles do not. Orthogonal roles (User Access,
            /// Navigation) match exactly and do not stack.
            /// </summary>
            public static bool HasScopedAccess(System.Security.Claims.ClaimsPrincipal principal, string scope, string minimumRole = User)
            {
                if (string.IsNullOrEmpty(scope)) return false;

                if (IsOrthogonalRole(minimumRole))
                    return principal.IsInRole(Scoped(minimumRole, scope));

                var requiredLevel = Array.IndexOf(OperationalHierarchy, minimumRole);
                if (requiredLevel < 0) return false;

                foreach (var role in OperationalHierarchy.Skip(requiredLevel))
                {
                    if (principal.IsInRole(Scoped(role, scope)))
                        return true;
                }

                return false;
            }

            /// <summary>
            /// Roles that can currently be assigned to a user. Global Manager/User are
            /// intentionally hidden — only scoped variants are offered for now.
            /// User Access is orthogonal (Users directory for that scope). Navigation is
            /// Intranet-only. Organizations uses Manager for archive/delete (same ladder as Tyme).
            /// </summary>
            public static IReadOnlyList<string> Assignable() => new[]
            {
                Admin,
                Scoped(UserAccess, Scopes.Tyme),
                Scoped(Manager, Scopes.Tyme),
                Scoped(Editor, Scopes.Tyme),
                Scoped(User, Scopes.Tyme),
                Scoped(UserAccess, Scopes.Intranet),
                Scoped(Navigation, Scopes.Intranet),
                Scoped(Editor, Scopes.Intranet),
                Scoped(User, Scopes.Intranet),
                Scoped(UserAccess, Scopes.Organizations),
                Scoped(Manager, Scopes.Organizations),
                Scoped(Editor, Scopes.Organizations),
                Scoped(User, Scopes.Organizations),
                Scoped(UserAccess, Scopes.Expenses),
                Scoped(Manager, Scopes.Expenses),
                Scoped(User, Scopes.Expenses),
            };

            /// <summary>True when <paramref name="role"/> is in <see cref="Assignable"/>.</summary>
            public static bool IsAssignableRole(string role) =>
                !string.IsNullOrWhiteSpace(role) && Assignable().Contains(role, StringComparer.Ordinal);

            /// <summary>
            /// Subset of <see cref="Assignable"/> the given admin is permitted to assign.
            /// Global Admin gets the full list; a scoped Admin gets only roles inside their scopes.
            /// </summary>
            public static IReadOnlyList<string> AssignableFor(System.Security.Claims.ClaimsPrincipal admin)
            {
                var scopes = AdministeredScopes(admin);
                if (scopes.Count == 0) return Array.Empty<string>();
                if (scopes.Contains(GlobalScopeWildcard)) return Assignable();
                return Assignable()
                    .Where(r =>
                    {
                        var i = r.IndexOf(':');
                        if (i <= 0) return false;
                        if (!scopes.Contains(r.Substring(i + 1))) return false;
                        // Only global Admin may grant User Access.
                        if (string.Equals(r[..i], UserAccess, StringComparison.Ordinal))
                            return false;
                        return true;
                    })
                    .ToList();
            }

            /// <summary>
            /// True if the principal has the global Admin role or any UserAccess:scope role.
            /// Use this to gate the Users directory.
            /// </summary>
            public static bool IsAnyAdmin(System.Security.Claims.ClaimsPrincipal principal)
            {
                if (principal == null) return false;
                if (IsGlobalAdmin(principal)) return true;
                var prefix = UserAccess + ":";
                foreach (var c in principal.Claims)
                {
                    if (c.Type != System.Security.Claims.ClaimTypes.Role) continue;
                    if (c.Value.StartsWith(prefix, StringComparison.Ordinal)) return true;
                }
                return false;
            }

            /// <summary>
            /// True only if the principal has the unscoped global Admin role. User Access
            /// holders return false. Use this for actions that should never be
            /// delegated to a scope owner — e.g. creating/deleting any user, purging another
            /// user's OIDC token or Google grant.
            /// </summary>
            public static bool IsGlobalAdmin(System.Security.Claims.ClaimsPrincipal principal)
            {
                if (principal == null) return false;
                foreach (var c in principal.Claims)
                {
                    if (c.Type != System.Security.Claims.ClaimTypes.Role) continue;
                    if (c.Value == Admin) return true;
                }
                return false;
            }

            /// <summary>
            /// Scopes the principal administers. Returns ["*"] for a global Admin
            /// (meaning "every scope, including global"). Empty if not any kind of admin.
            /// </summary>
            public static IReadOnlyCollection<string> AdministeredScopes(System.Security.Claims.ClaimsPrincipal principal)
            {
                if (principal == null) return Array.Empty<string>();
                var hasGlobal = false;
                var scopes = new HashSet<string>(StringComparer.Ordinal);
                foreach (var c in principal.Claims)
                {
                    if (c.Type != System.Security.Claims.ClaimTypes.Role) continue;
                    var v = c.Value;
                    if (v == Admin) hasGlobal = true;
                    else if (v.StartsWith(UserAccess + ":", StringComparison.Ordinal))
                        scopes.Add(v.Substring(UserAccess.Length + 1));
                }
                return hasGlobal ? new[] { GlobalScopeWildcard } : (IReadOnlyCollection<string>)scopes;
            }

            /// <summary>
            /// Does the user appear in the admin's filtered list?
            ///
            /// Any admin — global or User Access (e.g. UserAccess:Tyme) — can see every user in the
            /// directory, including users who also hold a global or out-of-scope role.
            /// Visibility is intentionally wide open so a scoped admin can find anyone.
            /// Role updates use <see cref="TryMergeRoleUpdate"/> (only the caller's module
            /// changes; other roles stay). Account-level mutations still use
            /// <see cref="CanManageUser"/> / <see cref="CanChangeUserActiveStatus"/>.
            /// </summary>
            public static bool IsVisibleTo(System.Security.Claims.ClaimsPrincipal admin, IEnumerable<string> targetRoles)
            {
                var scopes = AdministeredScopes(admin);
                return scopes.Count > 0;
            }

            /// <summary>
            /// Can the admin actually manage (mutate) the target user? Stricter than
            /// <see cref="IsVisibleTo"/>: every role on the target must be inside the
            /// admin's administered scopes.
            /// </summary>
            public static bool CanManageUser(System.Security.Claims.ClaimsPrincipal admin, IEnumerable<string> targetRoles)
            {
                var scopes = AdministeredScopes(admin);
                if (scopes.Count == 0) return false;
                if (scopes.Contains(GlobalScopeWildcard)) return true;
                if (targetRoles is null) return true;
                foreach (var r in targetRoles)
                {
                    var i = r.IndexOf(':');
                    if (i < 0) return false; // a global role on target requires global admin
                    if (!scopes.Contains(r.Substring(i + 1))) return false;
                }
                return true;
            }

            /// <summary>
            /// Locking a user out (inactive) or restoring login is workspace-wide.
            /// Scoped admins can assign roles in their module; they cannot deactivate anyone.
            /// </summary>
            public static bool CanChangeUserActiveStatus(System.Security.Claims.ClaimsPrincipal principal)
                => IsGlobalAdmin(principal);

            /// <summary>
            /// Should this user appear in manager Tyme team surfaces (Management,
            /// team submissions, etc.)? User-directory visibility (<see cref="IsVisibleTo"/>)
            /// is wider — any admin can see every account on Users. Team surfaces stay
            /// Tyme-scoped: a global Admin sees everyone; everyone else (scoped Admin or
            /// Manager:Tyme) sees users who have at least one Tyme-scoped role.
            /// <see cref="CanManageUser"/> is wrong here — it only applies to admins and
            /// would hide the whole team from Managers.
            /// </summary>
            public static bool IsVisibleInTymeTeamView(
                System.Security.Claims.ClaimsPrincipal viewer,
                IEnumerable<string> targetRoles)
            {
                if (IsGlobalAdmin(viewer))
                    return true;

                return HasOperationalRoleInScope(targetRoles, Scopes.Tyme);
            }

            /// <summary>
            /// Who may load other people's tasks on Reports. <c>Manager:Tyme+</c> always can
            /// (they already have Management). Other Tyme-scoped users can only when
            /// <paramref name="allowUsers"/> is true
            /// (<see cref="SettingKeys.TymeAllowUserTeamReports"/>).
            /// </summary>
            public static bool CanViewTymeTeamReports(
                System.Security.Claims.ClaimsPrincipal principal,
                bool allowUsers)
            {
                if (principal == null) return false;
                if (HasScopedAccess(principal, Scopes.Tyme, Manager)) return true;
                if (!HasScopedAccess(principal, Scopes.Tyme, User)) return false;
                return allowUsers;
            }

            /// <summary>True when the role list includes any role scoped to <paramref name="scope"/>.</summary>
            public static bool HasRoleInScope(IEnumerable<string> roles, string scope)
            {
                foreach (var role in roles)
                {
                    var i = role.IndexOf(':');
                    if (i > 0 && string.Equals(role[(i + 1)..], scope, StringComparison.Ordinal))
                        return true;
                }

                return false;
            }

            /// <summary>
            /// True when the list includes an operational (User/Editor/Manager) role in
            /// <paramref name="scope"/>. User Access / Navigation do not count.
            /// </summary>
            public static bool HasOperationalRoleInScope(IEnumerable<string> roles, string scope)
            {
                foreach (var role in roles)
                {
                    var i = role.IndexOf(':');
                    if (i <= 0) continue;
                    if (!string.Equals(role[(i + 1)..], scope, StringComparison.Ordinal)) continue;
                    if (IsOperationalRole(role[..i])) return true;
                }

                return false;
            }

            /// <summary>
            /// Global Admin can assign anything. User Access in a scope can assign operational
            /// and orthogonal-work roles in that scope, but cannot grant User Access itself.
            /// </summary>
            public static bool CanAssignRole(System.Security.Claims.ClaimsPrincipal admin, string role)
            {
                if (!IsAssignableRole(role)) return false;
                var scopes = AdministeredScopes(admin);
                if (scopes.Count == 0) return false;
                if (scopes.Contains(GlobalScopeWildcard)) return true;
                var i = role.IndexOf(':');
                if (i < 0) return false;
                if (string.Equals(role[..i], UserAccess, StringComparison.Ordinal))
                    return false;
                return scopes.Contains(role[(i + 1)..]);
            }

            /// <summary>
            /// Builds the role set to persist for an admin's user update.
            /// <paramref name="requestedRoles"/> replaces only roles the caller
            /// <see cref="CanAssignRole">may assign</see>. Current roles outside that
            /// set stay so Tyme User Access can add <c>Manager:Tyme</c> without stripping
            /// Organizations or Intranet. A global Admin may drop catalog-unknown
            /// leftovers (true legacy). Returns false if the caller is not an admin, if
            /// any requested role is outside their assignable set, or if the result
            /// would leave the user with no roles.
            /// </summary>
            public static bool TryMergeRoleUpdate(
                System.Security.Claims.ClaimsPrincipal admin,
                IEnumerable<string> currentRoles,
                IEnumerable<string> requestedRoles,
                out IReadOnlyList<string> merged)
            {
                merged = Array.Empty<string>();
                if (!IsAnyAdmin(admin)) return false;

                var requested = new List<string>();
                var seenRequested = new HashSet<string>(StringComparer.Ordinal);
                foreach (var role in requestedRoles ?? Array.Empty<string>())
                {
                    if (string.IsNullOrWhiteSpace(role)) continue;
                    if (!CanAssignRole(admin, role)) return false;
                    if (seenRequested.Add(role)) requested.Add(role);
                }

                var result = new List<string>();
                var seen = new HashSet<string>(StringComparer.Ordinal);
                var dropUnknown = IsGlobalAdmin(admin);

                foreach (var role in currentRoles ?? Array.Empty<string>())
                {
                    if (string.IsNullOrWhiteSpace(role)) continue;
                    if (CanAssignRole(admin, role)) continue;
                    if (dropUnknown && !IsAssignableRole(role)) continue;
                    if (seen.Add(role)) result.Add(role);
                }

                foreach (var role in requested)
                {
                    if (seen.Add(role)) result.Add(role);
                }

                if (result.Count == 0) return false;
                merged = result;
                return true;
            }
        }

        public static class SettingKeys
        {
            public const string AllowUserDelete = "AllowUserDelete";
            public const string DataRetentionDays = "DataRetentionDays";
            public const string AllowOrganizationDelete = "AllowOrganizationDelete";
            public const string AllowProjectDelete = "AllowProjectDelete";
            public const string TymeSubmissionMonthInterval = "TymeSubmissionMonthInterval";
            public const string TymeAllowManagerTimeCorrection = "TymeAllowManagerTimeCorrection";
            /// <summary>
            /// When true, Manager:Tyme / Admin:Tyme can submit an employee's month on their behalf.
            /// Default off until enabled under App Settings → Tyme.
            /// </summary>
            public const string TymeAllowManagerSubmitOnBehalf = "TymeAllowManagerSubmitOnBehalf";
            /// <summary>
            /// When true, any Tyme user can pick other employees on Reports (read-only).
            /// Manager:Tyme+ can always do this. Default off until enabled under App Settings → Tyme.
            /// </summary>
            public const string TymeAllowUserTeamReports = "TymeAllowUserTeamReports";
            /// <summary>Alias or Direct — workspace uses one mode only.</summary>
            public const string TymeManagerCorrectionMode = "TymeManagerCorrectionMode";

            /// <summary>
            /// Employee-facing default for Tasks/Calendar/Reports:
            /// their | adjusted | both (see <c>EmployeeTimeDisplayModeRules</c>).
            /// </summary>
            public const string TymeEmployeeTimeDisplayMode = "TymeEmployeeTimeDisplayMode";
            public const string TymeCalendarBackfillDefaultDays = "TymeCalendarBackfillDefaultDays";
            public const string TymeCalendarBackfillPromptUser = "TymeCalendarBackfillPromptUser";
            /// <summary>
            /// When true (default), Tyme entry UIs collect start time of day.
            /// When false, dialogs/grids are date + duration only; calendar/stopwatch still use real times.
            /// </summary>
            public const string TymeTrackTimeOfDay = "TymeTrackTimeOfDay";
            public const string WorkdayHours = "WorkdayHours";
            public const string TeamAvailabilityCalendarId = "TeamAvailabilityCalendarId";
            public const string ContactTypes = "ContactTypes";

            /// <summary>Workspace display name shown in the UI (setup wizard / branding).</summary>
            public const string AppDisplayName = "App:DisplayName";

            /// <summary>When true, first-run setup wizard is complete.</summary>
            public const string SetupCompleted = "Setup:Completed";

            /// <summary>Comma-separated allowed Google email domains, or * for any verified account.</summary>
            public const string AuthAllowedEmailDomains = "Auth:AllowedEmailDomains";

            /// <summary>When true, API rate limiting is active (see App Settings → General).</summary>
            public const string RateLimitEnabled = "RateLimitEnabled";
            /// <summary>Max API requests per minute per signed-in user (general routes).</summary>
            public const string RateLimitAuthenticatedPerMinute = "RateLimitAuthenticatedPerMinute";
            /// <summary>Max API requests per minute per IP when not signed in.</summary>
            public const string RateLimitAnonymousPerMinute = "RateLimitAnonymousPerMinute";
            /// <summary>Max failed/invalid Bearer attempts per IP per minute.</summary>
            public const string RateLimitInvalidBearerPerMinute = "RateLimitInvalidBearerPerMinute";
            /// <summary>Max POST /users/provision calls per IP per minute.</summary>
            public const string RateLimitProvisionPerMinute = "RateLimitProvisionPerMinute";
            /// <summary>Max external image fetch POSTs per user per minute.</summary>
            public const string RateLimitFetchExternalImagePerMinute = "RateLimitFetchExternalImagePerMinute";
            /// <summary>Max document upload POSTs per user per minute.</summary>
            public const string RateLimitUploadPerMinute = "RateLimitUploadPerMinute";
            /// <summary>Max heavy-read GETs (logs, data extraction) per user per minute.</summary>
            public const string RateLimitHeavyReadPerMinute = "RateLimitHeavyReadPerMinute";

            // Intranet
            public const string IntranetDriveParentFolderId = "IntranetDriveParentFolderId";
            /// <summary>Maximum nesting depth for curated intranet sidebar nav (top-level = 1).</summary>
            public const string IntranetNavigationMaxDepth = "IntranetNavigationMaxDepth";
            /// <summary>Allowed editor file types and max upload sizes in MB, e.g. png:5,pdf:25,docx:15.</summary>
            public const string IntranetImageMaxMegabytesByExtension = "IntranetImageMaxMegabytesByExtension";

            /// <summary>
            /// OrganizationId of the home company. Set by workspace Admin under
            /// App Settings → Organizations. Used wherever the app needs the home company.
            /// </summary>
            public const string HomeOrganizationId = "HomeOrganizationId";

            /// <summary>
            /// Personal-car mileage reimbursement rate (USD per mile). Default 0.555.
            /// </summary>
            public const string ExpensesMileageRatePerMile = "ExpensesMileageRatePerMile";

            /// <summary>
            /// Google Drive folder ID for the private Expenses root. Not the Intranet folder.
            /// </summary>
            public const string ExpensesDriveParentFolderId = "ExpensesDriveParentFolderId";
            public const string ExpensesApprovalSignature = "ExpensesApprovalSignature";
            public const string ExpensesApprovalSignatureMime = "ExpensesApprovalSignatureMime";
        }

        public static class Scopes
        {
            public const string Tyme = "Tyme";
            public const string Intranet = "Intranet";
            /// <summary>
            /// Organizations/Departments/Contacts. Deliberately its own scope, not part of Tyme —
            /// organization data is meant to be usable outside time tracking too. Read access
            /// (looking an org up, e.g. for a Project's org picker) is open to any authenticated
            /// user regardless of scope; this scope only gates the Organizations management page
            /// and mutations. See AuthGates.RequireOrganizations.
            /// </summary>
            public const string Organizations = "Organizations";

            /// <summary>
            /// Employee expense reports and receipts. Independent of Tyme — reimbursements
            /// against the home company, not client/project time. Global Admin does not pass.
            /// </summary>
            public const string Expenses = "Expenses";
        }

        public static class Claims
        {
            public const string UserId = "sub";
            /// <summary>AspNetUsers.Id from provision — distinct from Google's "sub".</summary>
            public const string AppUserId = "app_user_id";
            public const string Fullname = "FullName";

        }

        public static class API
        {
            public const string ClientName = "My.Workspace.API";

            /// <summary>
            /// Unified Tasks list — server-merged, sorted, and paged stopwatch work items + manual
            /// entries. A dedicated top-level route (not a trackedtasks/... sibling) so it never
            /// collides with the trackedtasks/{id} route.
            /// </summary>
            
            public static class Setup
            {
                public const string Api = "setup";

                /// <summary>GET — anonymous setup status (no secrets).</summary>
                public const string Status = $"{Api}/status";

                /// <summary>POST — write setup config before first user exists.</summary>
                public const string Configure = $"{Api}/configure";
            }

            public static class TaskList
            {
                public const string Api = "tasklist";

                public const string Get = Api;
            }

            public static class TrackedTask
            {
                public const string Api = "trackedtasks";

                public const string Get = Api;

                /// <summary>All rows in a date window — one round-trip for calendar/reports (no paging).</summary>
                public const string GetRange = $"{Api}/range";

                public const string GetActive = $"{Api}/active";

                public const string GetById = $"{Api}/";

                public const string Create = Api;

                public const string Delete = Api;

                public const string Update = Api;

                public const string Duplicate = Api;

                /// <summary>PUT — manager direct in-place correction (Manager:Tyme+; task month must be submitted).</summary>
                public const string ManagerCorrection = $"{Api}/";

                /// <summary>DELETE — revert a direct correction and restore the employee's original values.</summary>
                public const string DeleteManagerCorrection = $"{Api}/";
            }

            public static class StopwatchItem
            {
                public const string Api = "stopwatchitems";

                public const string Get = Api;

                /// <summary>GET — sessions in a from/to UTC range plus their work items, for Day view.</summary>
                public const string GetDay = $"{Api}/day";

                public const string Create = Api;

                public const string Update = Api;

                public const string CreateAndStart = Api;

                public const string Start = Api;

                public const string Stop = Api;

                public const string Sessions = Api;

                public const string Delete = Api;

                /// <summary>POST {id}/clear — removes the item from the Work Items list without
                /// deleting it or its sessions. See StopwatchItemFunction.ClearStopwatchItemAsync.</summary>
                public const string Clear = Api;
            }

            public static class Project
            {
                public const string Api = "projects";

                public const string Get = Api;

                // Flat route — nested "projects/lookup" is swallowed by some Functions hosts.
                public const string Lookup = "projectlookup";

                public const string GetById = $"{Api}/";

                public const string Create = Api;

                public const string Delete = Api;

                public const string DeleteImpact = $"{Api}/";

                public const string BillableImpact = $"{Api}/";

                public const string Update = Api;

                public const string SetActive = Api;

                public const string Archive = Api;
            }

            public static class Organization
            {
                public const string Api = "organizations";

                public const string Get = Api;

                // Flat route — nested "organizations/lookup" is swallowed by some Functions hosts.
                public const string Lookup = "organizationlookup";

                public const string GetById = $"{Api}/";

                public const string Create = Api;

                public const string Delete = Api;

                public const string Update = Api;

                public const string SetActive = Api;

                public const string Archive = Api;
            }

            public static class Department
            {
                public const string Api = "departments";

                public const string Get = Api;

                public const string GetById = $"{Api}/";

                public const string Create = Api;

                public const string Delete = Api;

                public const string Update = Api;

                public const string SetActive = Api;

                public const string Archive = Api;
            }

            public static class Contact
            {
                public const string Api = "contacts";

                public const string Get = Api;

                public const string Create = Api;

                public const string Delete = Api;

                public const string Update = Api;
            }

            public static class User
            {
                public const string Api = "users";

                public const string Get = Api;

                public const string Create = Api;

                public const string Update = Api;

                public const string SetActive = Api;

                public const string Archive = Api;

                public const string Delete = Api;

                public const string Provision = $"{Api}/provision";

                /// <summary>POST /api/users/{id}/purge-token — global Admin only.</summary>
                public const string PurgeToken = Api;

                /// <summary>POST /api/users/{id}/purge-permissions — global Admin only.</summary>
                public const string PurgePermissions = Api;

                /// <summary>Relative path for a single user, e.g. DELETE /api/users/{id}.</summary>
                public static string ById(string userId) => $"{Api}/{userId}";

                /// <summary>Relative path for POST actions on a single user, e.g. setactive, archive.</summary>
                public static string ActionPath(string userId, string action) => $"{Api}/{userId}/{action}";
            }

            public static class ProjectGroup
            {
                public const string Api = "projectgroups";

                public const string Get = Api;

                public const string GetById = $"{Api}/";

                public const string Create = Api;

                public const string Delete = Api;

                public const string Update = Api;
            }

            public static class AppSettings
            {
                public const string Api = "appsettings";

                public const string Get = Api;

                public const string Update = Api;

                public const string ContactTypeUsage = $"{Api}/contact-types/usage";
            }

            public static class Logs
            {
                // Single-segment route mirrors the admin-only "appsettings" sibling. We
                // moved off "admin/logs" because the deployed Functions host silently
                // dropped that route — the "admin" segment overlaps with the host's
                // own admin endpoints, even though the public URL is /api/admin/...
                public const string Api = "applogs";

                /// <summary>GET — recent App Insights logs (global Admin only). Query: hours, top.</summary>
                public const string Get = Api;

                /// <summary>
                /// Always requests Verbose+ so the table is not pre-filtered. Severity is shown
                /// per row; Function host write level is configured in Azure, not here.
                /// </summary>
                public static string Construct(int hours, int top, string? topic = null)
                {
                    var url = $"{Get}?hours={hours}&level=Verbose&top={top}";
                    if (!string.IsNullOrWhiteSpace(topic))
                        url += $"&topic={Uri.EscapeDataString(topic)}";
                    return url;
                }
            }

            public static class TimeSubmission
            {
                public const string Api = "timesubmissions";

                /// <summary>GET — current user's submissions (list).</summary>
                public const string Get = Api;

                /// <summary>GET — current user's overdue (unsubmitted, prior, has-tracked-time) months.</summary>
                public const string GetOverdue = $"{Api}/overdue";

                /// <summary>GET — current user's submittable months: unsubmitted, has-tracked-time,
                /// including the current/future months (early submission). See EligibleMonthDto.</summary>
                public const string GetEligible = $"{Api}/eligible";

                /// <summary>GET — Manager:Tyme+ team view: per-(user × month) row with status.
                /// Optional query: ?status=submitted|unsubmitted|all, ?userId=, ?year=, ?month=.</summary>
                public const string GetTeam = $"{Api}/team";

                /// <summary>POST — submit a single (Year, Month) for the current user.</summary>
                public const string Create = Api;

                /// <summary>POST — Manager:Tyme+ submit a month for another user (setting-gated).</summary>
                public const string CreateOnBehalf = $"{Api}/onbehalf";

                /// <summary>DELETE — unsubmit by id (Manager:Tyme / Admin:Tyme only). Global Admin does not satisfy this gate.</summary>
                public const string Delete = $"{Api}/";


                /// <summary>GET — alias/direct corrections for a submission month (manager reconciliation wizard).</summary>
                public const string GetCorrections = $"{Api}/";

                /// <summary>POST — unsubmit with optional alias reconciliation (apply vs keep originals).</summary>
                public const string Unsubmit = $"{Api}/";
            }

            public static class TrackedTaskAlias
            {
                public const string Api = "trackedtaskaliases";

                /// <summary>GET — manager-only list of aliases (optionally filtered by ?userId &amp; ?from &amp; ?to).</summary>
                public const string Get = Api;

                /// <summary>PUT — upsert the alias for a given task id (Manager:Tyme+ only; task's month must be submitted).</summary>
                public const string Upsert = $"{Api}/";

                /// <summary>DELETE — remove the alias for a given task id (Manager:Tyme+ only).</summary>
                public const string Delete = $"{Api}/";
            }

            public static class UserSettings
            {
                public const string Api = "usersettings";

                public const string Get = Api;

                public const string Update = Api;
            }

            public static class Expenses
            {
                public const string Api = "expenses";
                public const string Context = $"{Api}/context";
                public const string Settings = $"{Api}/settings";
                public const string Reports = $"{Api}/reports";
                public const string ReportById = $"{Api}/reports/";
                public const string Team = $"{Api}/team";
                public const string Data = $"{Api}/data";

                public static string LineReceipts(string reportId, string lineId) =>
                    $"{Api}/reports/{reportId}/lines/{lineId}/receipts";

                public static string ReceiptMedia(string receiptId) =>
                    $"{Api}/receipts/{receiptId}/media";

                public static string ReceiptById(string receiptId) =>
                    $"{Api}/receipts/{receiptId}";

                public static string Submit(string reportId) =>
                    $"{Api}/reports/{reportId}/submit";

                public static string Unsubmit(string reportId) =>
                    $"{Api}/reports/{reportId}/unsubmit";

                public static string Reimburse(string reportId) =>
                    $"{Api}/reports/{reportId}/reimburse";

                public static string UndoReimburse(string reportId) =>
                    $"{Api}/reports/{reportId}/undo-reimburse";

                public static string Pdf(string reportId, bool legacy = false) =>
                    legacy
                        ? $"{Api}/reports/{reportId}/pdf?layout=legacy"
                        : $"{Api}/reports/{reportId}/pdf";

                public const string Signature = $"{Api}/settings/signature";
                public const string SignatureMedia = $"{Api}/settings/signature/media";
                public const string MySignature = $"{Api}/signature";
                public const string MySignatureMedia = $"{Api}/signature/media";

                public static string ConstructUrlForTeam(
                    string status = "all",
                    string? userId = null,
                    int? year = null,
                    int? month = null,
                    IEnumerable<int>? years = null,
                    IEnumerable<int>? months = null)
                {
                    var parts = new List<string>
                    {
                        $"status={Uri.EscapeDataString(status)}"
                    };
                    if (!string.IsNullOrWhiteSpace(userId))
                        parts.Add($"userId={Uri.EscapeDataString(userId)}");
                    var yearList = (years ?? [])
                        .Concat(year is int y ? [y] : Array.Empty<int>())
                        .Where(v => v > 0)
                        .Distinct()
                        .ToList();
                    var monthList = (months ?? [])
                        .Concat(month is int m ? [m] : Array.Empty<int>())
                        .Where(v => v is >= 1 and <= 12)
                        .Distinct()
                        .ToList();
                    if (yearList.Count > 0)
                        parts.Add($"years={string.Join(",", yearList)}");
                    if (monthList.Count > 0)
                        parts.Add($"months={string.Join(",", monthList)}");
                    return $"{Team}?{string.Join("&", parts)}";
                }

                public static string ConstructUrlForData(
                    IEnumerable<string> entities,
                    string status,
                    int? year,
                    int? month,
                    IEnumerable<string>? userIds = null)
                {
                    var parts = new List<string>
                    {
                        $"Entities={string.Join(",", entities)}",
                        $"Status={Uri.EscapeDataString(status)}"
                    };
                    if (year.HasValue) parts.Add($"Year={year.Value}");
                    if (month.HasValue) parts.Add($"Month={month.Value}");
                    if (userIds is not null)
                    {
                        var ids = userIds.Where(id => !string.IsNullOrWhiteSpace(id)).ToList();
                        if (ids.Count > 0)
                            parts.Add($"UserIds={string.Join(",", ids.Select(Uri.EscapeDataString))}");
                    }

                    return $"{Data}?{string.Join("&", parts)}";
                }
            }

            public static class GoogleCalendar
            {
                public const string Api = "googlecalendar";

                /// <summary>GET — returns the OAuth URL for the user to visit to authorize calendar access.</summary>
                public const string GetAuthUrl = $"{Api}/authurl";

                /// <summary>GET (OAuth redirect target) — Google sends the user here with ?code=... &amp; ?state=...</summary>
                public const string Callback = $"{Api}/callback";

                /// <summary>POST — revokes the stored refresh token and clears calendar linkage.</summary>
                public const string Disconnect = $"{Api}/disconnect";

                /// <summary>POST ?from=YYYY-MM-DD&amp;to=YYYY-MM-DD — pushes the user's tracked tasks
                /// in the date range onto their primary Google calendar. Idempotent: tasks already
                /// on the calendar are skipped.</summary>
                public const string Backfill = $"{Api}/backfill";

                /// <summary>POST — records that the user answered the one-time post-connect backfill prompt.</summary>
                public const string AcknowledgeBackfillPrompt = $"{Api}/backfill/acknowledge";

                /// <summary>POST ?from=YYYY-MM-DD&amp;to=YYYY-MM-DD[&amp;userId=…] — pulls events from
                /// Google Calendar in the range and imports matched-slug ones into Tyme as TrackedTasks
                /// (dual-publishing to team availability where applicable). Self-service for the caller;
                /// global Admin can target another user via <c>userId</c>. The fix-it path for missed webhooks.</summary>
                public const string PullFromGoogle = $"{Api}/pullfromgoogle";

                /// <summary>POST (called by Google) — push notification from Google Calendar when an event changes.</summary>
                public const string Webhook = $"{Api}/webhook";

                /// <summary>
                /// Storage queue the webhook enqueues onto. The queue trigger imports
                /// after SQL is up. Azure queue names are lowercase + hyphens.
                /// </summary>
                public const string ImportQueue = "google-calendar-import";

                /// <summary>
                /// Blob container on the same storage account for per-user import leases.
                /// </summary>
                public const string ImportLockContainer = "google-calendar-import-locks";

                /// <summary>GET — connected users and watch health (global Admin).</summary>
                public const string SyncStatus = $"{Api}/syncstatus";

                /// <summary>POST — write a test message to the import queue (global Admin).</summary>
                public const string ProbeQueue = $"{Api}/probequeue";

                /// <summary>POST — re-register due/expired Google push watches (global Admin).</summary>
                public const string RenewWatches = $"{Api}/renewwatches";

                /// <summary>Channel id used by Admin → Test import queue. Not a real Google watch.</summary>
                public const string ProbeChannelId = "__admin-probe__";

                /// <summary>
                /// Builds the URL for <see cref="PullFromGoogle"/> with required from/to and optional
                /// target user (admin-only). Dates serialize as YYYY-MM-DD.
                /// </summary>
                public static string ConstructPullFromGoogle(DateTime from, DateTime to, string? targetUserId = null)
                {
                    var url = $"{PullFromGoogle}?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}";
                    if (!string.IsNullOrEmpty(targetUserId))
                        url += $"&userId={Uri.EscapeDataString(targetUserId)}";
                    return url;
                }
            }

            public static class GoogleDrive
            {
                public const string Api = "googledrive";

                /// <summary>GET — OAuth URL for Intranet Drive (browse/attach). Incremental on the Calendar grant.</summary>
                public const string GetAuthUrl = $"{Api}/authurl";

                /// <summary>POST — exchanges the Drive consent code; does not start Calendar sync.</summary>
                public const string Callback = $"{Api}/callback";
            }

            public static class AppDrive
            {
                public const string Api = "appdrive";
                public const string Status = $"{Api}/status";
                public const string GetAuthUrl = $"{Api}/authurl";
                public const string Callback = $"{Api}/callback";
                public const string Disconnect = $"{Api}/disconnect";
                public const string EnsureLayout = $"{Api}/ensurelayout";
            }

            public static class Analytics
            {
                public const string Api = "analytics";

                public const string GetDashboard = $"{Api}/dashboard";

                /// <summary>GET — Manager:Tyme+ all users' tasks for Management.</summary>
                public const string GetAllUsersTrackedTasks = $"{Api}/alluserstasks";

                /// <summary>GET — Tyme employees the caller may include on team Reports / Management.</summary>
                public const string GetManageableEmployees = $"{Api}/manageableemployees";

                /// <summary>GET — other users' tasks for Reports when team-report viewing is allowed.</summary>
                public const string GetTeamReports = $"{Api}/teamreports";

                /// <summary>GET — Manager:Tyme entity-centric table extract for Data Extraction.</summary>
                public const string GetTymeDataExtraction = $"{Api}/dataextraction";

                public static string ConstructUrlForAllUsersTasks(DateTime? from, DateTime? to) =>
                    ConstructUrlWithDateRange(GetAllUsersTrackedTasks, from, to);

                public static string ConstructUrlForTeamReports(
                    DateTime? from,
                    DateTime? to,
                    IEnumerable<string>? userIds = null)
                {
                    var url = ConstructUrlWithDateRange(GetTeamReports, from, to);
                    if (userIds == null) return url;
                    var ids = userIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().ToList();
                    if (ids.Count == 0) return url;
                    var joined = string.Join(",", ids.Select(Uri.EscapeDataString));
                    return url.Contains('?', StringComparison.Ordinal)
                        ? $"{url}&UserIds={joined}"
                        : $"{url}?UserIds={joined}";
                }

                public static string ConstructUrlForTymeDataExtraction(
                    IEnumerable<string> entities,
                    DateTime? from,
                    DateTime? to,
                    bool includeArchived = false,
                    string? organizationId = null,
                    string? projectGroupId = null,
                    string? projectId = null,
                    IEnumerable<string>? userIds = null)
                {
                    var parts = new List<string>
                    {
                        $"Entities={string.Join(",", entities)}"
                    };
                    if (from.HasValue) parts.Add($"From={from.Value:yyyy-MM-dd}");
                    if (to.HasValue) parts.Add($"To={to.Value:yyyy-MM-dd}");
                    if (includeArchived) parts.Add("IncludeArchived=true");
                    if (!string.IsNullOrEmpty(organizationId)) parts.Add($"OrganizationId={Uri.EscapeDataString(organizationId)}");
                    if (!string.IsNullOrEmpty(projectGroupId)) parts.Add($"ProjectGroupId={Uri.EscapeDataString(projectGroupId)}");
                    if (!string.IsNullOrEmpty(projectId)) parts.Add($"ProjectId={Uri.EscapeDataString(projectId)}");
                    if (userIds is not null)
                    {
                        var ids = userIds.Where(id => !string.IsNullOrWhiteSpace(id)).ToList();
                        if (ids.Count > 0)
                            parts.Add($"UserIds={string.Join(",", ids.Select(Uri.EscapeDataString))}");
                    }

                    return $"{GetTymeDataExtraction}?{string.Join("&", parts)}";
                }

                private static string ConstructUrlWithDateRange(string url, DateTime? from, DateTime? to)
                {
                    var parts = new List<string>();
                    if (from.HasValue) parts.Add($"From={from.Value:yyyy-MM-dd}");
                    if (to.HasValue) parts.Add($"To={to.Value:yyyy-MM-dd}");
                    return parts.Count > 0 ? $"{url}?{string.Join("&", parts)}" : url;
                }
            }

            public static class Intranet
            {
                public const string Api = "intranet";

                public static class Pages
                {
                    public const string Api = $"{Intranet.Api}/pages";

                    public const string Get = Api;

                    public const string GetById = $"{Api}/";

                    public const string GetBySlug = $"{Api}/slug/";

                    public const string Create = Api;

                    public const string Update = Api;

                    public const string Delete = Api;

                    public const string Reorder = $"{Api}/reorder";

                    public const string Move = $"{Api}/move";

                    // Not under /pages/search — that path is captured by GET /intranet/pages/{pageId}.
                    public const string Search = $"{Intranet.Api}/search/pages";

                    // Document actions scoped to a page (enables "create google doc or upload from page" UX)
                    public const string AttachDocument = $"{Api}/"; // POST /intranet/pages/{pageId}/documents
                    public const string DetachDocument = $"{Api}/"; // DELETE /intranet/pages/{pageId}/documents/{documentId}
                    public const string CreateGoogleDocument = $"{Api}/"; // POST /intranet/pages/{pageId}/documents/create
                    public const string UploadDocument = $"{Api}/"; // POST /intranet/pages/{pageId}/documents/upload
                }

                public static class Navigation
                {
                    public const string Api = $"{Intranet.Api}/navigation";

                    public const string Get = Api;

                    public const string GetById = $"{Api}/";

                    public const string Create = Api; // intranet admin only

                    public const string Update = Api;

                    public const string Delete = Api;

                    public const string Reorder = $"{Api}/reorder";
                }

                public static class Documents
                {
                    public const string Api = $"{Intranet.Api}/documents";

                    public const string Get = Api; // list/search curated docs

                    public const string GetById = $"{Api}/";

                    public const string Register = Api; // POST to register existing Drive file

                    public const string DriveBrowse = $"{Api}/drive"; // live browse of configured parent folder

                    /// <summary>GET {Api}/drive/{driveFileId}/media — stream Drive file bytes for editor/page images.</summary>
                    public const string DriveMedia = $"{Api}/drive/";

                    public const string Upload = $"{Api}/upload"; // upload to Drive + register in library

                    public const string MediaPolicy = $"{Intranet.Api}/media-policy";

                    public const string FetchExternalImage = $"{Intranet.Api}/fetch-external-image";

                    /// <summary>DELETE {Api}/drive/{driveFileId} — purge from pages, library, and Drive.</summary>
                    public const string DeleteDrive = $"{Api}/drive/";
                }
            }
        }
    }
}
