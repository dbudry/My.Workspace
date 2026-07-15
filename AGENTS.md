# My.Workspace – Agent Rules (Grok / Claude)

These rules apply to all sessions working in this repository. They are derived from the project's own documentation (README, docs/DEVELOPMENT.md, docs/ARCHITECTURE.md) and existing code patterns. Follow them strictly.

## Core Architecture & Tech Stack
- Blazor WebAssembly (My.Client) + Isolated Azure Functions (My.AzureFunction, .NET 10) + EF Core + Azure SQL.
- Google OIDC (id_token + access_token) for authentication.
- Two-way Google Calendar sync for tracked tasks (primary calendar only, using `[slug]` tags).
- Scoped roles: `Admin:Tyme`, `Manager:Tyme`, `User:Tyme` (plus global `Admin` for system maintenance).
- Caching: `ProjectsCache`, `OrganizationsCache`, `AppSettingsCache` — must be invalidated on mutations.
- Mapping: Mapperly (source-generated). Never ignore unmapped properties without reason.
- Hosting: Azure Static Web Apps (frontend) + Azure Functions Consumption.

## Always Use Constants (One Source of Truth)
- All API route strings live in `My.Shared/Constants.API.<Resource>`.
- Roles use `Constants.Roles.Scoped(role, scope)` or the helpers:
  - `HasScopedAccess(principal, scope, minimumRole)` — default for module surfaces.
  - `HasAccess(...)` — permissive (global roles also pass).
  - `IsAnyAdmin`, `IsVisibleTo`, `CanManageUser`, `CanAssignRole`.
- Never hard-code route strings or role names like `"Admin:Tyme"`.

## Authorization Pattern (AuthGates)
- Module endpoints (Tyme + future) **must** start with:
  ```csharp
  if (AuthGates.RequireScopedTyme(principal, out var userId) is IActionResult unauth)
      return unauth;
  ```
- This returns **401** (no valid identity) or **403** (authenticated but no permission).
- Global Admin-only or cross-cutting endpoints may use the permissive `Constants.Roles.HasAccess(...)`.
- The SPA's `UnauthorizedDelegatingHandler` treats 401 as "session expired → sign in", 403 as "no permission snackbar".
- Impersonation: `X-Impersonate-Role` header is handled by middleware for global admins.

## API Validation (FluentValidation)

All mutation endpoints use FluentValidation for request **shape** — never inline
`dto == null` or field-length checks in functions.

- Validators: `My.Shared/Validation/` (`AbstractValidator<T>` per DTO).
- Gate: `My.AzureFunction/Helpers/RequestValidator.cs`
  (`ReadJsonAndValidateAsync`, `BadRequestIfInvalidAsync`, query parsers).
- DI: `AddValidatorsFromAssemblyContaining<CreateStopwatchItemDtoValidator>(Singleton)` in `Program.cs`.
- Inject `IValidator<TDto>` into the function constructor.
- Business rules (DB lookups, submission month, slug uniqueness, active/archived
  state) stay in the function **after** FluentValidation passes.
- Context-dependent shape rules use `RootContextData` + `ValidationContextKeys`
  (see `ContactFunction`).
- Add tests in `My.Tests/Validation/` for new validators; extend
  `AzureFunctionsValidationDiTests` if adding a new validator type that must
  resolve from DI.

## Data & Caching Rules
- After any mutation that affects `ProjectsCache`, `OrganizationsCache`, or `AppSettingsCache`, call `*.Invalidate()` on the client before the call returns.
- Denormalized fields (e.g. organization name/color on Project DTOs) require cache invalidation on the parent entity too.
- Use `IRepository<T>` from the factory for simple CRUD. Use `ApplicationDbContext` directly only when you need joins/projections.

## Adding a New Resource / Entity (Standard Flow)
1. Entity in `My.DAL/Models` + `DbSet` + `OnModelCreating` config.
2. `dotnet ef migrations add ...` (with both `--project My.DAL --startup-project My.AzureFunction`).
3. DTOs in `My.Shared/Dtos/<Folder>/` (CreateXDto, UpdateXDto, XDto). Keep flat; denormalize only parent name/color when consumers need it.
4. Validators in `My.Shared/Validation/` (`CreateXDtoValidator`, `UpdateXDtoValidator`).
5. Mapperly partials in `MappingProfile.cs`.
6. New `XFunction.cs` in `My.AzureFunction/Functions/` — auth gate, then `RequestValidator.ReadJsonAndValidateAsync`, then business rules.
7. Register routes in `My.Shared/Constants.API`.
8. Client page using `PageHeader`, `SectionCard`, `PageLoader`, `EmptyState`, `<ScopedAuthorizeView Scope="Tyme" ...>`.
9. Update NavMenu (gated).

## UI / Blazor Conventions
- Use shared layout components: `<PageHeader>`, `<SectionCard>`, `<PageLoader>`, `<EmptyState>`, `<FilterBar>`.
- Search fields: `Immediate="true" DebounceInterval="300"`.
- Row actions: Prefer `<MudMenu Icon="@Icons.Material.Filled.MoreVert">` when >2 actions.
- Errors: Use `Snackbar.AddApiError(ex, "Couldn't...")` extension (strips JSON noise, handles 401/403 gracefully).
- For scoped module pages, wrap with `<ScopedAuthorizeView Scope="Tyme" ScopedOnly="true">`.

## Testing & Quality

### When agents must run tests (do not skip)
After **any** code change in this repo, agents must verify the work before finishing — do not hand off untested edits.

1. **Always build** the project(s) you touched:
   ```powershell
   dotnet build <Project>.csproj
   ```
2. **Always run targeted tests** when they exist for the area you changed:
   ```powershell
   dotnet test My.Tests/My.Tests.csproj --filter "FullyQualifiedName~<RelatedTests>"
   ```
   Example: sidebar accordion → `--filter "FullyQualifiedName~SidebarNavAccordionStateTests"`.
3. **Run the full suite** when you change any of:
   - `My.Shared/` (constants, rules, DTOs, navigation helpers)
   - `My.AzureFunction/` (especially `AuthGates`, functions, middleware)
   - `My.DAL/` (models, migrations, queries)
   - Authorization, roles, or cross-cutting client handlers
   ```powershell
   dotnet test My.Tests/My.Tests.csproj
   ```
4. **UI-only changes** (`*.razor`, CSS): build `My.Client` and add or extend unit tests for any logic you extract (see `My.Shared/Navigation/SidebarNavAccordionState.cs` — nav expand/collapse must not live only in Razor code-behind without tests).
5. If tests fail, fix them (or the implementation) before reporting the task complete. Do not tell the user to run tests instead of running them yourself.

### Test conventions
- Add tests in `My.Tests/` for pure logic (role helpers, rules, auth gates, FluentValidation validators, name heuristics, calendar parsing, sidebar accordion state, etc.).
- `dotnet test` must pass before merge/deploy.
- Mapperly warnings are compile-time — fix them.
- Smoke test in CI: unauthenticated POST to a protected endpoint must return 401.
- Run `dotnet build My.Client/My.Client.csproj -warnaserror` for strict checks when touching the client.

## Database & Migrations
- Always run EF commands from repo root with both project flags:
  ```powershell
  dotnet ef migrations add YourName --project My.DAL --startup-project My.AzureFunction
  dotnet ef database update --project My.DAL --startup-project My.AzureFunction
  ```
- CI runs `database update` automatically before deploy.

## Google Calendar & Self-Healing
- Events are only ingested when title contains a project `[slug]`.
- Nightly job (`PullMissedEventsNightly`) re-scans a rolling window.
- Watch channels auto-renew daily.

## Git / Branching / Commits
- `development` is the integration branch (CI only — build + test).
- `master` is the production branch (deploy + GitHub Release on merge).
- Bump `<Version>` in `My.Client/My.Client.csproj` before merging `development → master`.
- Feature branches are short-lived.
- Follow conventional commits when possible.
- PRs go through review; smoke test failure blocks deploy.

## When Editing Auth / Roles / Permissions
- Changes to `AuthGates.cs`, role helpers in `Constants.Roles`, or the delegating handlers are high-impact.
- Update the corresponding tests in `My.Tests/Authorization/` and `My.Tests/Roles/`.
- Keep the 401-vs-403 distinction clear for the SPA handlers.
- Document any new impersonation or scoping behavior.

## General
- Prefer existing patterns (e.g. `My.Shared/Rules/` folder for domain rules like `UserNameRules`, `ProjectColorRules`).
- Self-healing and defensive code is valued (name-from-email fallback, token recovery, nightly backfills).
- Keep the client lean — most business logic and rules live on the server or in Shared.
- For any new initiative or module, create a short doc in `docs/initiatives/`.
- Intranet (shipped): see `docs/initiatives/intranet.md` — Quill editor, Drive inserts,
  curated sidebar nav, favorites. Do not document the old `contenteditable` / `wysiwyg.js` path.

These rules take precedence over generic defaults. When in doubt, re-read `docs/DEVELOPMENT.md`, `docs/ARCHITECTURE.md`, `docs/initiatives/intranet.md`, and the "Conventions in one paragraph" section of the root README.
