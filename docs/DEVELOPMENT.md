# Development guide

How to get a local environment running, the conventions to follow when adding code,
and the workflow for shipping a change.

## Prerequisites

| Tool | Version | Why |
|---|---|---|
| .NET SDK | 10.0+ | Builds all four projects |
| Azure Functions Core Tools | v4 | `func start` for the API |
| Docker Desktop | recent | **Recommended** for SQL Server (portable dev DB; no need to install SQL/LocalDB on your machine) |
| (optional) SQL Server LocalDB/Developer | - | Only if you deliberately avoid the Docker path |
| EF Core CLI | `dotnet tool install --global dotnet-ef` | Migrations |
| Git | any | Source control |
| Visual Studio 2022+ or VS Code | optional | Either works; solution opens in either |

You'll also need a **Google Cloud project** with an OAuth 2.0 client ID configured
for OIDC (web application type, redirect URIs `http://localhost:7047/authentication/login-callback`
and the production equivalent). Calendar + Drive access is requested automatically as part of
the sign-in flow for new users (with a fallback "Connect / Reconnect" in Settings for repair).
The `/settings` redirect URI is still used for the Google OAuth callback.

### Enable Google Calendar + Drive APIs

The intranet editor browses your company Drive folder for images and file links. Calendar
sync also requires its API. **Both APIs must be enabled** in the same Google Cloud project
that owns your OAuth client ID — otherwise Drive browse calls return errors like
`Google Drive API has not been used in project … or it is disabled`.

1. Open [Google Cloud Console](https://console.cloud.google.com/) and select the project
   that contains your OAuth client (the project number appears in API error messages).
2. Go to **APIs & Services → Library**.
3. Search for and **Enable** each of:
   - [Google Calendar API](https://console.cloud.google.com/apis/library/calendar-json.googleapis.com)
   - [Google Drive API](https://console.cloud.google.com/apis/library/drive.googleapis.com)
4. Wait a few minutes for the change to propagate.
5. In the app, open **Settings** and use **Connect / Reconnect Google** if Drive or Calendar
   still fails after enabling (refreshes OAuth consent with the updated scopes).

> **Tip:** If you see HTTP 502/503 from `/api/intranet/documents/drive`, check the
> Functions host console — the underlying message is usually `accessNotConfigured` until
> Drive API is enabled.

## First-time setup

```bash
git clone <repo-url>
cd My.Workspace

# Restore + build everything
dotnet restore
dotnet build
```

**DB**: The recommended path is the Docker SQL container (no local SQL install required).
Just run one of the start scripts below — it will:
- Pull the mcr.microsoft.com/mssql/server:2022-latest image if needed
- Create (or reuse) a container named `my-workspace-mssql` on host port 14333
- On first run / after reset: wait through the one-time system DB upgrade (5-12 minutes is normal)
- Create the `MyWorkspace_Dev` database + enable READ_COMMITTED_SNAPSHOT
- Update `My.AzureFunction/local.settings.json` ConnectionStrings:DefaultConnection to point at the Docker instance

If a container already exists you get a 10-second "press R to reset (wipe data)" prompt.

You can still use a real local SQL or LocalDB if you want; just don't run the Docker setup scripts and manually keep the connection string in local.settings.json pointed at your instance.

Create `My.AzureFunction/local.settings.json` (gitignored) if it doesn't exist yet. The Docker setup script will overwrite the DefaultConnection for you. Minimum:

```json
{
  "IsEncrypted": false,
  "Values": {
    "AzureWebJobsStorage": "UseDevelopmentStorage=true",
    "FUNCTIONS_WORKER_RUNTIME": "dotnet-isolated",
    "Google__ClientId": "<your-google-client-id>.apps.googleusercontent.com"
  },
  "ConnectionStrings": {
    "DefaultConnection": "Server=(localdb)\\MSSQLLocalDB;Database=MyWorkspace_Dev;Trusted_Connection=True;MultipleActiveResultSets=true"
  },
  "Host": {
    "CORS": "*",
    "CORSCredentials": false
  }
}
```

Add the Calendar + Drive settings only if you want the automatic Google integration
(Calendar two-way sync for Tyme + Drive attachments in the Intranet editor). Without them
the core app still works for manual time entry, projects, etc.:

```jsonc
{
  "Values": {
    "Google__ClientSecret": "<oauth-client-secret>",
    "Google__TokenEncryptionKey": "<32-bytes-base64>"   // AES-GCM key for refresh tokens
  }
}
```

> **Key-name note.** The Function code reads these via
> `Environment.GetEnvironmentVariable("Google__ClientId")` etc., so the **double
> underscore** form is what `local.settings.json` and Azure App Settings need on
> Linux. The colon form (`Google:ClientId`) only works through `IConfiguration`,
> which is what `AuthMiddleware` uses for `ClientId` — both forms resolve to the
> same value.

Update `My.Client/wwwroot/appsettings.json` with your Google client ID — it's the
**same** ID the Function App uses, since they validate the same tokens.

### Google OAuth redirect URIs

The OAuth client in Google Cloud Console needs the following authorized redirect
URIs registered for each environment you'll exercise:

| Path | Used for |
|---|---|
| `/authentication/login-callback` | OIDC sign-in callback. |
| `/settings` | Google Calendar + Drive OAuth callback target. After the user grants access (automatically on first sign-in or via the reconnect button in Settings), Google redirects here with `?code=...`. The page posts it to the server callback and can return the user to their previous location. |

For local dev: `https://localhost:7047/authentication/login-callback` and
`https://localhost:7047/settings`. For production: the same two paths under
`https://your-app.example.com/`. The Azure default SWA hostname
(`https://<name>.azurestaticapps.net`) is also typically registered so testers can
exercise the auth flow on the staging URL.

## Running locally

**Preferred (one script does everything):**

```powershell
# From the repo root (or double-click Scripts\Dev-StartDebugSession.cmd)
# Right-click → "Run as administrator" the first time (for cert trust + Defender exclusions).
.\Scripts\Dev-StartDebugSession.ps1
```

This single script:
- Ensures the `my-workspace-mssql` Docker container (pulls image, creates, starts, waits for readiness, creates the dev DB, fixes the connection string).
- On existing container: gives you 10 seconds to press R to reset/wipe data.
- Cleans up previous build locks (Defender-aware).
- Starts Azurite in its own window.
- Starts the Functions host (port 7074, HTTPS by default) in its own window.
- Starts the Blazor client `dotnet watch` (port 7047) in its own window.
- (When run elevated) trusts the dev certs so https://localhost:7047 and the Functions host work cleanly in Edge/Chrome.

**First run of the SQL container after image pull or a reset will take 5-12 minutes** while SQL Server 2022 Express performs internal system database upgrades. The script prints dots and status messages and has a long timeout. You can watch progress with:

```powershell
docker logs my-workspace-mssql -f
```

The other scripts also drive pieces separately:

- `Dev-StartFunctionHost.ps1` — API only (7074)
- `Dev-StartClient.ps1` — **client only** (7047); use when the full session is already running and you only need to restart `dotnet watch` after a client fix (e.g. watch crashed, or you changed `wwwroot/js/`)

```powershell
.\Scripts\Dev-StartClient.ps1              # fast restart
.\Scripts\Dev-StartClient.ps1 -Clean         # if you see SRI / blazor.boot.json errors
.\Scripts\Dev-StartClient.ps1 -CleanShared   # also clean My.Shared + My.DAL
.\Scripts\Dev-StartClient.ps1 -NewWindow     # open watch in a separate console
```

**Note**: The old `Attach-To_FunctionWorker` script (and similar attach helpers) has been removed per project preference. Use `Dev-StartDebugSession.ps1` for first-time/full debugging; use `Dev-StartClient.ps1` to restart only the client when the rest of the stack is already up.

After the scripts launch, visit the URL reported by the client window (usually `https://localhost:7047`). Sign in with a `@example.com` Google account. The first user is auto-provisioned as global Admin.

> **Tip**: The Docker SQL container is configured with `--restart unless-stopped`, so it will come back after a Docker Desktop or machine restart. The 10-second reset prompt on every `Start-*-*.ps1` lets you easily get a clean DB when you want one.

> **Tip**: `dotnet watch` plus the WASM project rebuilds + reloads on `.razor` and
> `.razor.cs` changes within ~1 second. Function changes need a `func start` restart.

## Common commands

```bash
# Build whole solution
dotnet build

# Add an EF migration
dotnet ef migrations add AddSomething \
  --project My.DAL --startup-project My.AzureFunction

# Apply / roll back migrations
dotnet ef database update \
  --project My.DAL --startup-project My.AzureFunction
dotnet ef database update <PreviousMigrationName> \
  --project My.DAL --startup-project My.AzureFunction

# Remove the last migration (only if not yet applied to a shared DB)
dotnet ef migrations remove --project My.DAL --startup-project My.AzureFunction

# Force a clean build
dotnet build /t:rebuild

# Inspect what would deploy
dotnet publish My.Client/My.Client.csproj -c Release -o ./published-app
dotnet publish My.AzureFunction/My.AzureFunction.csproj -c Release -o ./published-api
```

## Project conventions

### API routes

All route strings live in `My.Shared/Constants.cs` under `Constants.API.<Resource>`.
**Always use them on the client.** Hardcoded paths break the moment someone reorganizes URLs.

**On the server, the `[HttpTrigger(Route = "…")]` must be a plain string literal** — a
`Constants.API.*` reference there silently resolves to no route and the endpoint 404s. Keep the
literal in sync with the constant by hand.

**Literal routes vs. `{id}` — the collision gotcha.** Azure Functions matches a single-segment
`{id}` route for *any* one-segment value, so a sibling literal on the **same HTTP method** gets
swallowed. Example: `GET trackedtasks/{id}` was matching `GET trackedtasks/range`, so the request
ran `GetById("range")` → 404 and never reached `GetTrackedTasksRange`. When you add a literal
sibling (`…/range`, `…/active`, …) next to a same-method `{id}` route, constrain `{id}` so it can't
match the literal:

```csharp
// excludes the literal GET siblings so they fall through to their own functions
Route = "trackedtasks/{id:regex(^(?!range$|active$).+$)}"
```

`TrackedTaskRouteConstraintTests` reads the route off the attribute via reflection and verifies the
constraint excludes the reserved words while still accepting real ids — copy it when you add more.
(A different-HTTP-method sibling, e.g. `POST …/create` next to `DELETE …/{id}`, does **not** collide.)

### Role checks

Always go through `Constants.Roles.*` helpers — never hand-roll a `IsInRole`
check. They handle the global vs. scoped distinction, including impersonation.

### API validation (FluentValidation)

Mutation endpoints validate request **shape** with FluentValidation before
business rules or DB writes. Do not add inline `if (dto == null)` or
`string.IsNullOrWhiteSpace` field checks in functions — use the shared gate
instead.

**Add a validator** when introducing a new `CreateXDto` / `UpdateXDto`:

1. Create `XValidators.cs` under `My.Shared/Validation/` (subclass
   `AbstractValidator<T>`).
2. Mirror client `DataAnnotations` and EF `MaxLength` where they exist; keep
   error messages user-facing.
3. In `XFunction.cs`, inject `IValidator<CreateXDto>` / `IValidator<UpdateXDto>`
   and call:

   ```csharp
   var (dto, validationError) = await RequestValidator.ReadJsonAndValidateAsync(req, createValidator);
   if (validationError != null) return validationError;
   ```

4. Put DB-dependent checks **after** validation (NotFound, submission rules,
   uniqueness, active/archived state).

**Query params** — build a small DTO (`YearMonthQueryDto`, `DateRangeQueryDto`)
and use `RequestValidator.ParseYearMonthQuery` / `ParseDateRangeQuery`, or
`BadRequestIfInvalid` for a single value.

**Context-dependent rules** — pass data via `RootContextData` in the configure
callback (see `ContactFunction` + `ValidationContextKeys`).

**Tests** — add unit tests in `My.Tests/Validation/` for non-trivial rules;
`AzureFunctionsValidationDiTests` confirms validators resolve from DI like
`Program.cs`.

### Caches & invalidation

If you add a mutation that affects data already in `ProjectsCache`,
`OrganizationsCache`, or `AppSettingsCache`, **call `Invalidate()` after the API
call returns successfully**. Mutations that change denormalized fields on a
neighbouring entity also need the neighbouring cache invalidated (e.g. renaming
an organization invalidates ProjectsCache because each project DTO carries the
org name + color).

### Page chrome

Use the shared layout components:

- `<PageHeader Title="…" Description="…" />`
- `<SectionCard Title="…">…</SectionCard>`
- `<PageLoader IsLoading="@isLoading">…</PageLoader>`
- `<EmptyState Icon="…" Title="…" Message="…" />` for "no records" placeholders
- `<FilterBar>` for filter rows on management/report pages

### Search inputs

Use `Immediate="true" DebounceInterval="300"` on `MudTextField` search boxes — keeps
filters responsive without re-rendering on every keystroke.

### Row actions on management tables

When a row has more than two actions, collapse them into a single
`<MudMenu Icon="@Icons.Material.Filled.MoreVert">` instead of a stack of
icon buttons + tooltips. This is the single biggest per-row render saving;
see ProjectManager / OrganizationManager for examples.

### Snackbar errors

Use the `Snackbar.AddApiError(ex, "Couldn't…")` extension (in
`My.Client/Extensions`) instead of `Snackbar.Add(ex.Message, …)`. It strips
cryptic JSON parse errors, suppresses 401 (already handled), and gives a
friendlier message for 5xx + timeouts.

### Intranet module notes (editor, navigation, Drive, scoping)

Full shipped summary: [`docs/initiatives/intranet.md`](initiatives/intranet.md).

- **Editor** (`IntranetEditor.razor`): Quill via `wwwroot/js/quill-editor.js` (vendored
  under `wwwroot/lib/quill/`, lazy-loaded on first editor open). No Markdown source +
  live preview — HTML is stored in `ContentMarkdown` and edited as formatted content.
- **Toolbar**: formatting + shortcuts (Ctrl+B, etc.); **image** / **folder** → Drive insert
  dialog (`IntranetContentInsertDialog`); **code** → full-page HTML view/edit/copy; **link**
  → custom dialog (Quill's default link handler is overridden to avoid Blazor navigation bugs).
- **Drive images**: `quillEditor.insertIntranetImage` preserves `data-drive-file-id`;
  `intranet-media.js` + `IntranetMediaService` hydrate private images in the editor and on
  `/intranet/pages/{id|slug}`.
- **Editor chrome**: Published toggle (drafts hidden from regular users), **View page**,
  **Save**, **Save & view**; opening from a page passes `fromView=1`.
- **Attachments tab**: tracks which page-linked Drive files are referenced in content.
- **Navigation admin** (`IntranetNavigation.razor`): `MudTreeView`, contextual create from
  parent items, title-based pickers. Separate from **page hierarchy** (Manage Pages).
- **Sidebar** (`NavMenu.razor` + `CuratedNavItem.razor`): recursive curated tree, accordion
  state in `My.Shared/Navigation/SidebarNavAccordionState.cs` (see `SidebarNavAccordionStateTests`).
- **Documents** (`IntranetDocuments.razor`, `IntranetFileBrowser`): company Drive folder from
  App Settings; library mode + picker mode for the editor.
- **Favorites**: `FavoriteIntranetPageIds` on user settings; star on page view; links on dashboard.
- **Roles**: Manage Pages / Documents → `Editor:Intranet+`; Navigation tree → `Admin:Intranet`.
  Strict `ScopedOnly` — global Admin alone has no Intranet access; use impersonation to test.
- **Legacy**: `wwwroot/js/wysiwyg.js` is unused (replaced by Quill); safe to delete in a cleanup PR.

## Adding a new resource (entity → DTO → API → page)

A typical flow when adding a new domain object:

1. **Entity** — add the class in `My.DAL/Models`. Configure in
   `ApplicationDbContext.OnModelCreating` (key, FK, indexes, cascade rules).
   Add a `DbSet<>` property.
2. **Migration** — `dotnet ef migrations add Add<EntityName>`. Inspect the generated
   `Up`/`Down`. If you need backfill SQL on existing rows, edit the migration to
   include `migrationBuilder.Sql("…")`.
3. **DTO** — add `CreateXDto`, `UpdateXDto`, `XDto` under `My.Shared/Dtos/<Folder>/`.
   Keep DTOs flat; denormalize parent names/colors only when consumers actually
   need them.
4. **Mapping** — add Mapperly partial methods in `MappingProfile.cs`, ignoring
   server-only properties. Run a build — Mapperly source-gen will tell you about
   any unmapped fields.
5. **Validators** — add `CreateXDtoValidator` / `UpdateXDtoValidator` in
   `My.Shared/Validation/`. See [API validation (FluentValidation)](#api-validation-fluentvalidation)
   above.
6. **API endpoints** — new file under `My.AzureFunction/Functions/`. Pattern:
   `XFunction` class, one `[Function("…")]` per route.
   - Always start with the auth gate. For module surfaces (Tyme, Intranet, future):
     `if (AuthGates.RequireScoped(principal, "Tyme" /* or "Intranet" */, out var userId, minRole) is IActionResult unauth) return unauth;`
     (or the `RequireScopedTyme` convenience wrapper). Returns 401 on no auth, 403 on no
     permission. For cross-cutting / system endpoints where global Admin should pass, use
     the permissive `Constants.Roles.HasAccess(...)` directly.
   - Deserialize + validate with `RequestValidator.ReadJsonAndValidateAsync`; keep
     business-rule checks (NotFound, submission month, uniqueness) after validation.
   - Use `IRepository<TEntity>` from `IRepositoryFactory` for simple CRUD; inject
     `ApplicationDbContext` directly when you need joins / projections.
   - On mutations that affect cached data, no server-side action needed — the
     client cache invalidates after the call returns successfully.
7. **Constants** — register routes under `Constants.API.<Resource>`.
8. **Client model** (optional) — add `My.Client/Models/<Entity>.cs` if the page
   needs computed display properties. Otherwise consume the DTO directly.
9. **Page** — `My.Client/Pages/.../<EntityManager>.razor` + `.razor.cs`. Inject
   `IHttpClientFactory`, `ISnackbar`, `IDialogService`. Reuse PageHeader /
   SectionCard / PageLoader / EmptyState. Add a NavMenu link, gated by
   `<ScopedAuthorizeView>` if non-admin users shouldn't see it.

## Testing

The `My.Tests/` xUnit project covers the parts of the system that are pure logic and
worth pinning down with assertions — role-check helpers, calendar-event date parsing,
the auth gate's 401/403 split, name-from-email heuristics, etc. Add a test alongside any
feature change that lands in `My.Shared` (including `My.Shared/Validation/`),
`My.AzureFunction/Authorization/`, or `My.AzureFunction/Helpers/RequestValidator.cs`.
Smoke
tests run in CI against the deployed Function App (an unauthenticated POST that should
401). Locally:

- `dotnet test` is the cheapest signal — should be 0 failures. Builds run as part of
  the test command.
- The Mapperly generator surfaces unmapped properties at compile time — pay attention
  to its warnings.
- For the WASM client, in-browser testing (`dotnet watch` + a real browser) is still
  the reality. Keep your changes scoped so manual testing covers what changed.

## Troubleshooting

| Symptom | Likely cause |
|---|---|
| Login redirect loop / "Unable to acquire token" | Google OIDC client ID mismatch between WASM `appsettings` and Function `local.settings.json`. They must be the same. |
| 400 on every API call | `AuthMiddleware` constructor failed — usually `Google:ClientId` missing in `local.settings.json`. Check `func start` console output. |
| 404 on a literal API route like `…/range` or `…/active` (while `…/{id}` works) | A same-method `{id}` route is swallowing the literal sibling. Constrain `{id}` with a regex that excludes the literal (see **API routes → Literal routes vs. `{id}`**). Restart the Functions host — `func start` does **not** hot-reload C#. |
| "User has data newer than retention period" on delete | Working as designed — flip `AllowUserDelete` in App Settings, or wait it out, or hard-delete the user's tracked tasks first. |
| EF migration fails with "no DbContext found" | Run from the repo root with both `--project My.DAL` and `--startup-project My.AzureFunction`. The startup project provides DI. |
| "X requires the global Admin role" on impersonation | A scoped admin can't impersonate. Sign in as a global Admin and pick the lower role-set from `/admin`'s impersonate dialog. |
| Calendar/Drive consent not appearing on first login | The auto-connect (route 2) only triggers on the dashboard landing (`/`) for a fresh authenticated session when `IsGoogleCalendarConnected` is false. Check localStorage `googleAutoConnectAttempted` and browser console. The connect still works manually from Settings. |
| WASM bundle huge / slow to load | Verify `service-worker.published.js` cache is in use — DevTools → Application → Service Workers. Asset hashing comes from `assetsManifest.version`. |
| `dotnet watch` exits with `NullReferenceException` in `DefinitionMap.GetPreviousMethodHandle` | Known hot-reload (Edit-and-Continue) bug in some SDK builds when `.razor` / JS interop changes land. Stop watch, run `dotnet build`, then restart `dotnet watch` (or use `Dev-StartDebugSession.ps1` which relaunches cleanly). Not an app runtime failure. |
| Quill link toolbar button reloads the page | Fixed by overriding Quill's default link handler in `quill-editor.js` — it must open the insert-link dialog, not Quill's built-in `<a href="">` tooltip. Hard-refresh the browser after pulling editor changes. |

## Branching & PRs

The convention so far:

- `development` is the **integration** branch. Merging to it runs build + test only.
- `master` is the **production** branch. Merging to it deploys to Azure and creates a
  GitHub Release (`v<Version>` from `My.Client/My.Client.csproj`).
- Bump `<Version>` before opening a PR `development → master` — the version gate blocks
  the merge otherwise.
- Feature branches are short-lived (`AvivaRequestedAdjustments`, `ApiErrorUX`, etc.)
  and merged via PR into `development`.
- The production deploy pipeline includes a smoke test step; if it fails, the deploy is
  considered broken even if the build succeeded.
