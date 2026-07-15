# Architecture

A more detailed look at how the pieces fit together. This doc complements the root
[`README.md`](../README.md) — start there for a high-level overview.

## High level

```
┌─────────────────────┐         ┌────────────────────────────┐         ┌──────────────┐
│  Azure Static Web   │  HTTPS  │   Azure Functions          │  TDS    │  Azure SQL   │
│  Apps               │ ──────► │   (.NET 10 isolated worker)│ ──────► │  MyWorkspace│
│  (Blazor WASM)      │  /api/* │                            │         │              │
└──────────┬──────────┘         └─────────────┬──────────────┘         └──────┬───────┘
           │                                  │                               │
           │         ┌────────────────────────▼──────────────┐                │
           │         │  AuthMiddleware (Google OIDC,         │                │
           │         │  validates id_token / access_token)   │                │
           │         └───────────────────────────────────────┘                │
           │                                                                  │
           │ OIDC redirect                                                    │
           │ (id_token + access_token)                                        │
           ▼                                                                  │
   ┌────────────────────┐                                                     │
   │  accounts.google   │                                                     │
   │  .com (OIDC)       │                                                     │
   └────────────────────┘                                                     │
                                                                              │
                                ┌─────────────────────────────────────────────┘
                                │
                       ┌────────▼────────┐
                       │  Google         │
                       │  Calendar API   │  (two-way sync via push channels)
                       └─────────────────┘
```

The Static Web App fronts the Function App at the same origin via the SWA
`/api/*` proxy — so production calls are same-origin and never see CORS. In local
development the WASM project hits `https://localhost:7071/api/` directly and CORS is
handled by `CorsMiddleware`.

## Request lifecycle (authenticated API call)

1. **Browser** holds a Google `id_token` and `access_token` from OIDC sign-in.
2. WASM sends the request with `Authorization: Bearer <token>`. Several
   `DelegatingHandler`s on the named `HttpClient` add headers and recover from
   failures:
   - `TokenExpiryDelegatingHandler` catches `AccessTokenNotAvailableException` and
     redirects to login.
   - `RetryDelegatingHandler` retries 5xx with backoff (covers cold starts).
   - `UnauthorizedDelegatingHandler` distinguishes session-expiry from permission failure:
     a 401 on an *unauthenticated* client triggers sign-in; a 401 (defensive — gates should
     emit 403) or 403 on an *authenticated* client surfaces a "no permission" snackbar
     with no redirect.
   - `ImpersonationDelegatingHandler` adds `X-Impersonate-Role` when an admin is
     viewing the app as a downgraded role-set.
3. **AuthMiddleware** validates the token (locally as a JWT against Google's signing
   keys, falling back to the `tokeninfo` endpoint with caching), looks up the
   matching `ApplicationUser`, and adds role claims (cached for 60s in
   `IMemoryCache`).
4. If `X-Impersonate-Role` is present and the real principal is a global Admin, the
   middleware **replaces** role claims with the impersonated set so endpoint
   authorization treats them as the lower role.
5. Function executes; its DI scope gets a fresh `ApplicationDbContext` per
   invocation.
6. **Request-shape validation** (FluentValidation) runs on mutation endpoints
   before business rules or DB writes — see [API validation](#api-validation).

## API validation

All Azure Function mutation endpoints use a two-layer validation model:

| Layer | Where | Examples |
|-------|-------|----------|
| **1 — Shape / format** | FluentValidation in `My.Shared/Validation/` | Required fields, string lengths, email format, slug shape, base64 payloads, year/month ranges, contact-type allowed list |
| **2 — Business rules** | Function code (or shared rule classes in `My.Shared/Rules/`) | Entity exists, month already submitted, project/org active state, slug uniqueness, self-action guards |

### Wiring

- Validators live in `My.Shared/Validation/` (one file per domain, e.g.
  `ProjectValidators.cs`, `IntranetValidators.cs`).
- `Program.cs` registers them via
  `AddValidatorsFromAssemblyContaining<CreateStopwatchItemDtoValidator>(Singleton)`.
- Functions inject `IValidator<TDto>` (or a concrete validator when multiple
  validators target the same type, e.g. `ReorderPagesRequestValidator` vs
  `ReorderRequestValidator`).
- `My.AzureFunction/Helpers/RequestValidator.cs` is the single gate:
  - `ReadJsonAndValidateAsync` — parse JSON + validate (replaces inline
    `dto == null` / field checks).
  - `BadRequestIfInvalidAsync` — validate an already-deserialized DTO; supports
    `RootContextData` for context-dependent rules (contact types).
  - `ParseYearMonthQuery` / `ParseDateRangeQuery` — query-string validation.

Failed shape validation returns **400** with a JSON **string array** of error
messages (`ValidationResultFormatter.ToMessages`).

### Context-dependent rules

Contact type validation loads the allowed list from the `ContactTypes` app
setting at runtime. The function passes it into the validator via
`ValidationContextKeys.AllowedContactTypes` and
`ValidationContextKeys.ContactTypeRequired` before calling
`ReadJsonAndValidateAsync`.

### What stays inline

Auth gates (`AuthGates`), NotFound/Conflict responses, submission-month rules,
`ValidateProjectIsLoggable`, slug uniqueness, delete-impact checks, and
Google/Drive token presence remain in function code — they need DB or request
context and are not FluentValidation candidates.

## Auth model

### Roles

- **Global**: `Admin` is for system-level maintenance — user provisioning, role
  assignment, app settings, infrastructure pages (Logs, App Settings). It does
  **not** automatically grant access to module data; a global Admin who needs to
  see Tyme content uses [impersonation](#impersonation) to assume a scoped role.
- **Scoped**: `Admin:Tyme` / `Manager:Tyme` / `User:Tyme` (and `Admin:Intranet` / `Editor:Intranet` / `User:Intranet`). These are the day-to-day module roles. A scoped admin can only see and mutate users whose roles fall **entirely** inside the scopes they administer. Global `Admin` alone does not grant Tyme or Intranet access.
- The hierarchy `User < Manager < Admin` applies inside a given scope.

All checks go through helpers in `Constants.Roles` so client and server agree:

| Helper | Use for |
|---|---|
| `HasScopedAccess(principal, scope, minimumRole)` | **Default for module surfaces.** "Can this caller use this Tyme endpoint?" — only scoped roles inside `scope` qualify; global Admin alone does not. |
| `HasAccess(principal, scope, minimumRole)` | Permissive variant — global roles also satisfy. Reserve for cross-cutting/system-level checks where global Admin should pass. |
| `IsAnyAdmin(principal)` | "Is this caller any kind of admin (global or scoped)?" |
| `IsVisibleTo(admin, targetRoles)` | "Should this user appear in the caller's user list?" — a global role on the target shields them from a scoped admin. |
| `CanManageUser(admin, targetRoles)` | Stricter — every role on the target must be in the admin's scopes. |
| `CanAssignRole(admin, role)` | "Is this caller allowed to assign this role?" |

Page-level UI gates use `<ScopedAuthorizeView Scope="Tyme" (or "Intranet") … ScopedOnly="true">`
(or the equivalent `[Authorize(Policy = "Tyme:Manager:Scoped")]` / `"Intranet:Editor:Scoped"`)
for module pages so that a global-Admin-only operator sees no nav entry and gets no
permissions. The default (permissive) `ScopedOnly="false"` is reserved for cross-cutting
surfaces where global Admin should pass.

API endpoints inside a module wrap the auth + permission split in
`AuthGates.RequireScopedTyme(principal, out var userId)`: returns `null` on pass, an
`UnauthorizedResult` (**401**) when the caller has no validated identity, or a
`StatusCodeResult(403)` when authenticated but lacking the scoped role. The split
matters because the SPA's handler interprets 401 as "session expired, redirect to
sign-in"; returning 401 for a permission failure on a still-valid Google session
would loop through Google right back to the same endpoint. The helper trusts the
`UserId` claim (only `AuthMiddleware` writes it, only after cryptographic Bearer
validation) rather than `principal.Identity.IsAuthenticated`, which can read the
Functions Worker host's anonymous identity instead of ours when multiple identities
are on the request.

### Impersonation

A real global Admin can pick a downgraded role-set from `/admin` (or anywhere via
the impersonate dialog) to test what a scoped user sees. `ImpersonationService`
stores the chosen roles in localStorage; the
`ImpersonationDelegatingHandler` adds `X-Impersonate-Role` to API calls;
`AuthMiddleware.ApplyImpersonation` strips the higher claims server-side and adds
the requested ones. Both client `<AuthorizeView>` and server `[Authorize]` honor
the downgrade — what you see is what an Admin:Tyme would see.

### Provisioning

`POST /api/users/provision` handles first login:

- If no users exist yet, the caller is provisioned as the **first global Admin**.
- If the user already exists and is active, returns their profile and bumps
  `LastSignInAt` (used by the OIDC session-invalidation check below).
- Otherwise (user not pre-created by an admin, or marked inactive/archived) → 403.

### Admin-initiated session invalidation

Two `ApplicationUser` columns power the global-Admin-only "Force re-sign-in" and
"Revoke Google permissions" actions on `/admin/users`:

- `LastSignInAt` — set by `/api/users/provision` on every successful sign-in.
- `OidcSessionInvalidatedAt` — set by `POST /api/users/{id}/purge-token` and
  `POST /api/users/{id}/purge-permissions`.

`AuthMiddleware` drops the request's identity (returning 401 from any function
that requires a `UserId` claim) when `LastSignInAt < OidcSessionInvalidatedAt`.
The `/api/users/provision` route is exempt from this check — it's the endpoint
that bumps `LastSignInAt` back past the invalidation, so a user who's been
purged can sign back in normally on their next round-trip through Google.

`purge-permissions` additionally revokes the user's Google calendar grant
(`oauth2.googleapis.com/revoke`), clears `UserSettings.GoogleRefreshToken` and
related fields, and clears `GoogleEventId` on every `TrackedTask` they own.
The user-initiated disconnect button does NOT call `revoke` — only the admin
purge does, because revoking is what invalidates the SPA's sign-in token and
that's the explicit intent of the admin action.

## Data model (key entities)

```
ApplicationUser ──┬──< TrackedTask ── Project ──┬── Organization ──< Department ──< Contact
                  │         │                   ├── ProjectGroup
                  │         │                   └── Department
                  │         └── TrackedTaskAlias (manager override per task)
                  │
                  └──< TimeSubmission (per User × Year × Month — locks edits)

UserSettings : 1-1 with ApplicationUser
AppSetting   : key/value store (workspace-wide flags)
```

Projects, ProjectGroups, Organizations, Departments, and Contacts are **workspace-shared
reference data** — there is no per-user ownership. Anyone with the right role
(Manager+ for projects/groups, Admin+ for orgs) sees and edits the same list.

### Notable cascade rules (from `ApplicationDbContext.OnModelCreating`)

- Delete **Organization** → cascades to its Departments + directly-attached Contacts.
  Projects under it survive but lose Organization/Department FKs (`SetNull`). Tracked
  tasks keep their project link.
- Delete **Department** → cascades to its Contacts. Projects under it lose
  Department FK (`SetNull`).
- Delete **Project** → tracked tasks survive but lose `ProjectId` (`SetNull`).
- Delete **ProjectGroup** → projects under it lose `ProjectGroupId` (`SetNull`).
- Delete **TrackedTask** → its TrackedTaskAlias cascades.
- Delete **User** → blocked unless their newest tracked task is past the retention
  window. The `DeleteUserAsync` function also explicitly removes their tracked tasks
  and UserSettings; TimeSubmissions cascade.

The descriptions on `/admin/appsettings` for each delete switch summarize the same
in user-facing language.

## Caching strategy

Three Scoped (per-circuit) caches in `My.Client/Services`:

| Cache | Source | Invalidates on |
|---|---|---|
| `ProjectsCache` | `GET /projects?PageSize=10000&includeArchived=true` | Project + ProjectGroup CRUD; Organization + Department CRUD (denormalized name/color/dept lives on project DTOs) |
| `OrganizationsCache` | `GET /organizations?PageSize=10000&includeArchived=true&summary=true` (lightweight — no Contacts/Departments includes) | Organization + Department CRUD |
| `AppSettingsCache` | `GET /appsettings` | AppSettings save |

All three use the same shape (`GetAsync`, `Invalidate`, `RefreshAsync`, `Changed`
event, in-flight Task coalescing).

OrganizationManager bypasses the org cache and fetches the **full** payload
(with Contacts + Departments + Department.Contacts) directly because that's the
only consumer that needs it. Project pickers and the Projects management page get
the lightweight version through the cache.

There's also a **server-side** in-memory cache in `AuthMiddleware` that holds
`(userId → roles)` for 60s, so we don't hit the DB on every API call to fetch
roles. `UserFunctions.UpdateUserAsync` busts this key after a role change so the
next request sees the new roles immediately.

## Tyme — time submission & manager aliases

Two cooperating concepts let users finalize their time without losing the ability
for managers to make corrections:

- **TimeSubmission** (per User × Year × Month) — when a user submits a month, all
  their TrackedTasks for that month become read-only. The TrackedTask CRUD
  endpoints reject create / update / delete when a submission exists for the
  task's month. Managers (`Manager:Tyme` / `Admin:Tyme` / global `Admin`) can
  unsubmit a single (user × month) row if a correction is needed — unsubmit is
  always per-employee, never bulk per-month.
- **TrackedTaskAlias** (one per TaskId) — a manager-only "shadow" record with
  override `StartDate`, `Duration`, and `ProjectId`. Aliases can only be created
  for tasks whose month is currently submitted (i.e. they're the post-submission
  correction tool). The Management view overlays aliases on top of
  originals; users never see them.

The interval setting (`TymeSubmissionMonthInterval`, default `1`) controls how
often employees are expected to submit. Overdue months show as a dashboard
banner for the user and a colored kebab badge in the nav menu's Submit link.

### Manager team view

`GET /api/timesubmissions/team` (Manager:Tyme+ only) returns one row per
(user × month) for users the caller can manage, with submitted/unsubmitted
status. Optional query filters: `?status=submitted|unsubmitted|all`,
`?userId`, `?year`, `?month`. Scope filtering routes through
`Constants.Roles.CanManageUser` so a Manager:Tyme can't reach users with
global or other-scope-only roles.

The shared `My.Client/Components/TimeSubmissions/TeamSubmissionsView.razor`
component renders this feed with a filter bar (status / employee / year /
month) and a per-row Unsubmit button. Two consumers:

- `/tyme/submit` — `ShowFilters="true"`, `DefaultStatusFilter="all"`. Lives
  below the user's own "Your months" section.
- Dashboard "Employees with unsubmitted time" widget — `ShowFilters="false"`,
  `DefaultStatusFilter="unsubmitted"`, `MaxRows="10"`.

This is the only feed for the manager team view; the older
`/api/timesubmissions/overdue/all` and `UserOverdueDto` were removed when the
shared component landed.

## Intranet — pages, nav, and Drive

The Intranet module is a scoped mini-site inside the WASM app. Full product notes:
[`docs/initiatives/intranet.md`](initiatives/intranet.md).

**Data model (two layers):**

- `IntranetPage` — content pages (HTML in `ContentMarkdown`), hierarchy via
  `ParentPageId`, optional slug, `IsPublished` for drafts.
- `IntranetNavigationItem` — curated sidebar menu (page link or `ExternalUrl`),
  tree via `ParentId`, independent of page hierarchy.

**API:** `IntranetFunction.cs` — CRUD for pages and nav, document register/upload/browse,
authenticated Drive media streaming (`GET …/drive/{id}/media`), and recursive nav DTOs
for the sidebar (draft pages filtered for non-editors).

**Client:**

- View: `IntranetPage.razor` — `MarkupString` HTML + `IntranetMediaService` hydration.
- Edit: `IntranetEditor.razor` — Quill (`quill-editor.js`, lazy-loaded), Drive insert
  dialogs, attachments tab, publish toggle, view/save navigation.
- Sidebar: `NavMenu.razor` + `CuratedNavItem.razor`; accordion state in
  `SidebarNavAccordionState` (shared + unit-tested).
- Favorites: `FavoriteIntranetPageIds` on `UserSettings`, dashboard quick links.

**Auth:** `RequireScoped(principal, "Intranet", …)` on all intranet endpoints.
`ScopedOnly="true"` on module pages. Global `Admin` does not pass Intranet gates.

**Not in v1:** page versioning, full-text search, forms builder, per-page ACLs beyond
module roles.

## Background jobs

Two TimerTrigger functions:

- **`GoogleCalendarWatchRenewalFunction`** — daily at 06:00 UTC. Renews any
  Google push channel that expires within 96 hours so the inbound calendar sync
  keeps flowing.
- **`KeepaliveFunction`** — every 5 minutes. Warms the Consumption-plan
  instance (well inside the ~20-min idle deallocation window) and runs a
  lightweight SQL `CanConnectAsync` so the connection path is not ice-cold
  after idle. Function executions sit inside the free monthly grant; SQL ping
  cost is negligible on provisioned Azure SQL (see DEPLOYMENT.md cost notes).

## Hosting notes

- Static Web App: `swa-my-workspace` in `rg-my-workspace`.
- Function App: `func-my-workspace` in the same RG. Consumption plan; Always-On
  is **not** used (cost) — the keepalive timer compensates.
- SQL: `your-sql.database.windows.net`, database `MyWorkspace`. Production
  authenticates via Azure AD (`Active Directory Default`).
- Routes / fallback configured in `My.Client/wwwroot/staticwebapp.config.json`.
  Robots are noindex'd globally; the WASM index is the SPA fallback for any
  non-`/_framework`, non-`/api`, non-asset path.
