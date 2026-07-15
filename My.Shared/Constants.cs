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

            /// <summary>
            /// Sentinel value used in <see cref="AdministeredScopes"/> to mean
            /// "global authority — manages every scope".
            /// </summary>
            public const string GlobalScopeWildcard = "*";

            /// <summary>
            /// Formats a scoped role, e.g. "Admin:Tyme". Pass null scope for a global role.
            /// </summary>
            public static string Scoped(string role, string? scope) =>
                string.IsNullOrEmpty(scope) ? role : $"{role}:{scope}";

            /// <summary>
            /// Permissive scope check: global roles (no scope) also satisfy. Reserve for
            /// cross-cutting/system-level checks. Module surfaces should use
            /// <see cref="HasScopedAccess"/> so global Admin doesn't automatically pick up
            /// module permissions — they use impersonation when they need module access.
            /// </summary>
            public static bool HasAccess(System.Security.Claims.ClaimsPrincipal principal, string scope, string minimumRole = User)
            {
                var roleHierarchy = new[] { User, Editor, Manager, Admin };
                int requiredLevel = Array.IndexOf(roleHierarchy, minimumRole);

                foreach (var role in roleHierarchy.Skip(requiredLevel))
                {
                    // Global role (e.g. "Admin") grants access to everything
                    if (principal.IsInRole(role))
                        return true;
                    // Scoped role (e.g. "Admin:Tyme") grants access to that scope
                    if (principal.IsInRole(Scoped(role, scope)))
                        return true;
                }

                return false;
            }

            /// <summary>
            /// Default gate for module surfaces: only a scoped role inside <paramref name="scope"/>
            /// satisfies the check; global roles do not. A global Admin who needs module
            /// access uses impersonation (X-Impersonate-Role) to assume a scoped role.
            /// </summary>
            public static bool HasScopedAccess(System.Security.Claims.ClaimsPrincipal principal, string scope, string minimumRole = User)
            {
                if (string.IsNullOrEmpty(scope)) return false;
                var roleHierarchy = new[] { User, Editor, Manager, Admin };
                int requiredLevel = Array.IndexOf(roleHierarchy, minimumRole);

                foreach (var role in roleHierarchy.Skip(requiredLevel))
                {
                    if (principal.IsInRole(Scoped(role, scope)))
                        return true;
                }

                return false;
            }

            /// <summary>
            /// Roles that can currently be assigned to a user. Global Manager/User are
            /// intentionally hidden — only scoped variants are offered for now.
            /// </summary>
            public static IReadOnlyList<string> Assignable() => new[]
            {
                Admin,
                // Tyme: User (track time), Manager (team reports/availability), Admin (orgs/projects).
                // Editor:Tyme is intentionally omitted — no Tyme surface gates on Editor; it would
                // behave like User:Tyme and only adds picker noise (same rationale as Manager:Intranet).
                Scoped(Admin, Scopes.Tyme),
                Scoped(Manager, Scopes.Tyme),
                Scoped(User, Scopes.Tyme),
                // Intranet scope (for the company knowledge base / mini site + Drive attachments).
                // For a small company (~20 people) with no approval workflows, we only expose
                // User / Editor / Admin. Manager:Intranet is intentionally omitted because
                // Editor is the practical "content contributor" role and Admin controls the
                // navigation structure. We do not want confusing duplicate roles that do the same thing.
                Scoped(Admin, Scopes.Intranet),
                Scoped(Editor, Scopes.Intranet),
                Scoped(User, Scopes.Intranet),
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
                        return i > 0 && scopes.Contains(r.Substring(i + 1));
                    })
                    .ToList();
            }

            /// <summary>
            /// True if the principal has the global Admin role or any Admin:scope role.
            /// Use this to gate user-management UI/endpoints.
            /// </summary>
            public static bool IsAnyAdmin(System.Security.Claims.ClaimsPrincipal principal)
            {
                if (principal == null) return false;
                foreach (var c in principal.Claims)
                {
                    if (c.Type != System.Security.Claims.ClaimTypes.Role) continue;
                    if (c.Value == Admin) return true;
                    if (c.Value.StartsWith(Admin + ":", StringComparison.Ordinal)) return true;
                }
                return false;
            }

            /// <summary>
            /// True only if the principal has the unscoped global Admin role. Scoped admins
            /// (Admin:Tyme etc.) return false. Use this for actions that should never be
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
                    else if (v.StartsWith(Admin + ":", StringComparison.Ordinal))
                        scopes.Add(v.Substring(Admin.Length + 1));
                }
                return hasGlobal ? new[] { GlobalScopeWildcard } : (IReadOnlyCollection<string>)scopes;
            }

            /// <summary>
            /// Does the user appear in the admin's filtered list?
            ///
            /// A scoped admin (e.g. Admin:Tyme) can only see users whose roles are
            /// fully inside their administered scopes. If the target has *any*
            /// global role (Admin / Manager / User without a scope), only a global
            /// admin can see them — global roles are super-roles that scoped admins
            /// can't reach. Within scope, any overlapping scoped role makes the
            /// target visible.
            /// </summary>
            public static bool IsVisibleTo(System.Security.Claims.ClaimsPrincipal admin, IEnumerable<string> targetRoles)
            {
                var scopes = AdministeredScopes(admin);
                if (scopes.Count == 0) return false;
                if (scopes.Contains(GlobalScopeWildcard)) return true;
                if (targetRoles is null) return false;

                // Any global role on the target shields them from a scoped admin.
                foreach (var r in targetRoles)
                {
                    if (r.IndexOf(':') < 0) return false;
                }

                foreach (var r in targetRoles)
                {
                    var i = r.IndexOf(':');
                    if (i < 0) continue;
                    if (scopes.Contains(r.Substring(i + 1))) return true;
                }
                return false;
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
            /// Should this user appear in manager Tyme team surfaces (Management,
            /// team submissions, etc.)? Scoped admins use <see cref="IsVisibleTo"/>.
            /// Scoped Managers (Manager:Tyme who are not any Admin) see every user with
            /// at least one Tyme-scoped role. <see cref="CanManageUser"/> is wrong here —
            /// it only applies to admins and would hide the whole team from Managers.
            /// </summary>
            public static bool IsVisibleInTymeTeamView(
                System.Security.Claims.ClaimsPrincipal viewer,
                IEnumerable<string> targetRoles)
            {
                if (IsAnyAdmin(viewer))
                    return IsVisibleTo(viewer, targetRoles);

                return HasRoleInScope(targetRoles, Scopes.Tyme);
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
            /// Is the admin allowed to assign the given role to a user? Global Admin can
            /// assign anything; a scoped Admin can only assign roles in their scopes.
            /// </summary>
            public static bool CanAssignRole(System.Security.Claims.ClaimsPrincipal admin, string role)
            {
                if (!IsAssignableRole(role)) return false;
                var scopes = AdministeredScopes(admin);
                if (scopes.Count == 0) return false;
                if (scopes.Contains(GlobalScopeWildcard)) return true;
                var i = role.IndexOf(':');
                if (i < 0) return false; // global role requires global admin
                return scopes.Contains(role.Substring(i + 1));
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
            /// <summary>Alias or Direct — workspace uses one mode only.</summary>
            public const string TymeManagerCorrectionMode = "TymeManagerCorrectionMode";
            public const string TymeCalendarBackfillDefaultDays = "TymeCalendarBackfillDefaultDays";
            public const string TymeCalendarBackfillPromptUser = "TymeCalendarBackfillPromptUser";
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
        }

        public static class Scopes
        {
            public const string Tyme = "Tyme";
            public const string Intranet = "Intranet";
        }

        public static class Claims
        {
            public const string UserId = "sub";
            /// <summary>AspNetUsers.Id from provision — distinct from Google's "sub".</summary>
            public const string AppUserId = "app_user_id";
            public const string LastLogin = "LastLogin";
            public const string Fullname = "FullName";
            public const string Role = "Role";

        }

        public static class API
        {
            public const string ClientName = "My.Workspace.API";

            public static class Setup
            {
                public const string Api = "setup";

                /// <summary>GET — anonymous setup status (no secrets).</summary>
                public const string Status = $"{Api}/status";

                /// <summary>POST — write setup config before first user exists.</summary>
                public const string Configure = $"{Api}/configure";
            }

            /// <summary>
            /// Unified Tasks list — server-merged, sorted, and paged stopwatch work items + manual
            /// entries. A dedicated top-level route (not a trackedtasks/... sibling) so it never
            /// collides with the trackedtasks/{id} route.
            /// </summary>
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

                public const string Create = Api;

                public const string Update = Api;

                public const string CreateAndStart = Api;

                public const string Start = Api;

                public const string Stop = Api;

                public const string Sessions = Api;

                public const string Delete = Api;
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

                public const string GetById = $"{Api}/";

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
                public static string Construct(int hours, int top) =>
                    $"{Get}?hours={hours}&level=Verbose&top={top}";
            }

            public static class TimeSubmission
            {
                public const string Api = "timesubmissions";

                /// <summary>GET — current user's submissions (list).</summary>
                public const string Get = Api;

                /// <summary>GET — current user's overdue (unsubmitted, prior, has-tracked-time) months.</summary>
                public const string GetOverdue = $"{Api}/overdue";

                /// <summary>GET — Manager:Tyme+ team view: per-(user × month) row with status.
                /// Optional query: ?status=submitted|unsubmitted|all, ?userId=, ?year=, ?month=.</summary>
                public const string GetTeam = $"{Api}/team";

                /// <summary>POST — submit a single (Year, Month) for the current user.</summary>
                public const string Create = Api;

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

            public static class Analytics
            {
                public const string Api = "analytics";

                public const string GetDashboard = $"{Api}/dashboard";

                /// <summary>GET — Manager:Tyme+ all users' tasks for Management.</summary>
                public const string GetAllUsersTrackedTasks = $"{Api}/alluserstasks";

                /// <summary>GET — Manager:Tyme+ list of employees the caller can manage in Tyme.</summary>
                public const string GetManageableEmployees = $"{Api}/manageableemployees";

                /// <summary>GET — Admin:Tyme entity-centric table extract for Data Extraction.</summary>
                public const string GetTymeDataExtraction = $"{Api}/dataextraction";

                public static string ConstructUrlForAllUsersTasks(DateTime? from, DateTime? to) =>
                    ConstructUrlWithDateRange(GetAllUsersTrackedTasks, from, to);

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
