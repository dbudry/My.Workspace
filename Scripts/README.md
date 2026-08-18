# Scripts

Helper scripts grouped by prefix:

| Prefix | Group | Scripts |
|--------|-------|---------|
| `Dev-` | Local development environment | Start full debug session, function host only, Docker SQL setup |
| `EfCore-` | EF Core **schema** migrations | Add migration, apply migrations to local DB |
| `
| `Import-Intranet-ItKnowledgeBase` | Intranet **Knowledge Base** seed | SQL + PowerShell; pages, nav, asset naming CSV |

EF Core migration **code** lives in `My.DAL/Data/Migrations/` — not in this folder.

## Dev- (local development)

| Script | Purpose |
|--------|---------|
| `Dev-StartDebugSession.ps1` / `.cmd` | **Preferred.** Docker SQL, Azurite, Functions (7074), Blazor client (7047 via shared boot helper), VS debugger attach. `-ClientFullReset` for stubborn WASM boot errors. |
| `Dev-StartFunctionHost.ps1` / `.cmd` | Functions API only — calls `Dev-SetupDockerSql.ps1`, then `func start` on 7074. |
| `Dev-StartClient.ps1` / `.cmd` | **Client only** — restart `dotnet watch` on 7047 (kills stale port, cleans output by default). Use when the full session is already up. |
| `Dev-SetupDockerSql.ps1` | SQL Server Docker container + updates `local.settings.json`. Called by the Dev-Start* scripts. |

```powershell
.\Scripts\Dev-StartDebugSession.ps1          # full stack (client: clean + watch)
.\Scripts\Dev-StartDebugSession.ps1 -ClientFullReset   # full stack + wipe client bin/obj
.\Scripts\Dev-StartFunctionHost.ps1          # API only
.\Scripts\Dev-StartClient.ps1                # client only (default: kill port + clean)
.\Scripts\Dev-StartClient.ps1 -NoClean       # fast restart, no clean
.\Scripts\Dev-StartClient.ps1 -FullReset     # delete bin/obj when SRI / 99% boot errors persist
.\Scripts\Dev-StartClient.ps1 -NewWindow     # client in a new console window
```

### Blazor stuck at 99% / “integrity” console errors

**Root cause (now fixed in `My.Client.csproj`):** with WASM asset fingerprinting on (the .NET 9/10
default), every rebuild changes the content-hashed `.wasm`/`.pdb` names. A browser holding an older
boot manifest then requests a fingerprint that no longer exists → **404 → empty body → SRI computes
the hash of empty (`47DEQpj8HBSa+/TImW+5JCeuQeRkm5NMpJWZG3hSuFU=`) → integrity mismatch → boot hangs
at 99%**. Debug builds now set `<WasmFingerprintAssets>false</WasmFingerprintAssets>` so filenames are
stable and a stale manifest still resolves to a real file. Release keeps fingerprinting for prod cache-busting.

If you still hit it (e.g. a second `dotnet` on port 7047, or a stale service worker):

1. Stop **all** clients on 7047 — only one `dotnet watch` should run. Avoid starting `dotnet run --project My.Client` in extra terminals while watch is up.
2. Run `.\Scripts\Dev-StartClient.ps1 -Clean` (or `-FullReset` to wipe client bin/obj).
3. Hard-refresh the browser (**Ctrl+Shift+R**) or close every tab for `localhost:7047`.
4. Prefer `Dev-StartDebugSession.ps1` or `Dev-StartClient.ps1` over ad-hoc `dotnet run`.

## EfCore- (schema migrations)

| Script | Purpose |
|--------|---------|
| `EfCore-AddMigration.ps1` | Create a new migration in `My.DAL/Data/Migrations`. |
| `EfCore-ApplyMigrations.ps1` | Apply pending migrations to local DB (`dotnet ef database update`). |
| `EfCore-BaselineMigrationHistory.ps1` | Stamp `__EFMigrationsHistory` when the DB schema is already current (after a squash). Use `-AllMigrations` if CI still deploys the old multi-migration chain. |

```powershell
.\Scripts\EfCore-AddMigration.ps1 AddYourFeatureName
.\Scripts\EfCore-ApplyMigrations.ps1
```

## 

Imports **business rows** from the legacy **

| Script | Purpose |
|--------|---------|
| `
| `
| `
| `
| `

### 

The script reads these five tables via `SELECT *` and expects at least these columns (extras are fine):

| 
|--------------|------------------|
| `Employees` | `EmployeeID`, `UserName`, `FirstName`, `LastName`, `IsActive` |
| `Customers` | `CustomerID`, `OrganizationName`, address fields, `ContactName`, `ContactTitle`, `PhoneNumber`, `MaintenanceContactName`, `MaintenanceContactTitle`, `MaintenanceContactEmail`, `Note` |
| `Projects` | `ProjectID`, `ProjectName`, `CustomerID` |
| `Billings` | `BillingID`, `BillingNote`, `HourValue`, `BillingDate`, `HourFK`, `ProjectFK`, `EmployeeFK` |
| `Hours` | `HourID`, `HourValue` |

### What gets imported / wiped

| Imported into MyWorkspace | From 
|----------------------------|-------------|
| `AspNetUsers` (unmatched employees) | `Employees` |
| `Organizations` + `Contacts` | `Customers` |
| `Projects` (ungrouped — no auto-created project group) | `Projects` |
| `TrackedTasks` + `TimeSubmissions` | `Billings` + `Hours` |

**Import modes:**

| Mode | Flag | Behavior |
|------|------|----------|
| Import & Append | `-Mode Append` | Keeps existing data. Imports only 
| Import & Wipe | `-Mode Wipe` | Clears selected data (see wipe scope), then re-imports from 

**Completion summary** prints two sections so Append is not confused with a full reload:

1. **This run** — `Found` (
2. **Totals now in MyWorkspace** — full table `COUNT(*)` after the run (not how many were appended).

**Wipe scope** (only when `-Mode Wipe`; prompted if omitted, 15s default **Tasks**):

| Scope | Flag | What is deleted |
|-------|------|-----------------|
| Tasks | `-WipeScope Tasks` | Tracked tasks, manager aliases, correction audits, time submissions, Billing import-map rows. Keeps orgs/projects. |
| Projects + Tasks | `-WipeScope ProjectsAndTasks` | Projects, project groups, tasks/aliases/submissions, Project + Billing map rows. Keeps organizations/contacts/departments. |
| All | `-WipeScope All` | Organizations, contacts, departments, projects, groups, tasks/aliases/submissions, full import map. |

Legacy aliases still accepted: `TimeOnly` → `Tasks`, `Full` / `Reference` → `All`.

**Never deleted by import (any mode/scope):** `AppSettings`, `UserSettings` (Google Calendar, preferences), Intranet content (pages, navigation, Drive folder id), Google-signed-in users with roles.

**Fixed defaults (not prompted):**

| Setting | Default | Notes |
|---------|---------|--------|
| Organization import | **Consolidated** | Same-name 
| Years of billing | **All** | Full 
| Users | **All** | Every employee. Optional override: `-ImportUsers "..."`. |
| Lifecycle sync | **Yes** | Archive stale orgs/projects after import. Optional override: `-LifecycleSync No`. |

If you omit `-Profile`, the script prompts for **L** (local Docker) or **P** (production) and defaults to **Local Docker** after 15 seconds.

If you omit `-Mode`, the script prompts for **W** or **A** and defaults to **Append** after 15 seconds.

When **Wipe** is selected, the script prompts for wipe scope (**T** / **P** / **A**, default Tasks). Interactive prompts end there.

**Production imports from your PC** need Azure SQL firewall access. On **Starlink** (or any dynamic IP), a one-time manual firewall rule will stop working when your IP changes. Two practical options:

1. **Local Docker (recommended for dev)** — import to `localhost:14333`; set up Docker once, never touch the firewall.
2. **Production with auto-firewall** — set `FunctionApp.SubscriptionId`, `FunctionApp.TenantId`, and `MyWorkspace.SqlSubscriptionId` in `

   Starlink **static IP** (business add-on) is optional if you prefer a permanent manual rule instead.

Both import modes preserve Intranet content, `AppSettings`, `UserSettings`, and Google-signed-in users with roles. Settings are never wiped.

**Dedup keys:** `EmployeeID`, `CustomerID`, `ProjectID`, and `BillingID`. (`HourFK` is a duration preset lookup, not a unique billing row.) The map table is cleared on **All** wipe; **Tasks** clears only Billing map rows; **ProjectsAndTasks** clears Project + Billing map rows.

**Employee archive on import:** inactive in 

**Not imported from 

**Examples:**

```powershell
# Append only — add 
.\Scripts\

# Refresh tracked tasks + aliases only — keep orgs, projects, all settings
.\Scripts\

# Wipe projects + tasks — keep organizations and settings
.\Scripts\

# Full business wipe (consolidated orgs + all years; settings still preserved)
.\Scripts\
```

### Google user email map


email (`gdelatorre@example.com`). Build a map from your Google Workspace user export:

```powershell
.\Scripts\
```

The import script loads `
Matched employees link to existing `AspNetUsers` by Google email; new users get the
Google email when mapped.

### Local Docker import

```powershell
# 1. Docker SQL + schema (if not already done)
.\Scripts\Dev-StartDebugSession.ps1    # or Dev-SetupDockerSql + EfCore-ApplyMigrations

# 2. Sign in once at https://localhost:7047 so your Google user exists in AspNetUsers

# 3. Copy config template and run import
Copy-Item .\Scripts\
.\Scripts\
.\Scripts\
# sa password default: DevSql!Passw0rd2026 (see Dev-SetupDockerSql.ps1)
```

### Production import (when ready)

```powershell
.\Scripts\
.\Scripts\
```

The script creates `
installs the `SqlServer` module if needed, caches prompted SQL passwords in config (unless
`-NoCachePassword`), ensures `az` is on the configured production subscription (re-logins if
needed), and tries to stop/start the Function App during the import (continues if the app or
resource group is not found). Pass `-SkipPauseFunctionApp` to skip the stop/start entirely.

## Import-Intranet-ItKnowledgeBase (IT Knowledge Base seed)

Idempotent seed for **15 draft pages** and **19 navigation items** under Knowledge Base
(BitLocker, Bitwarden, OpenVPN, GCPW, Email migration tree, Computer Inventory, Safe Software,
Zoho CRM). Safe to re-run — inserts only when fixed IDs are missing; does not overwrite migrated
HTML content.

| File | Purpose |
|------|---------|
| `Import-Intranet-ItKnowledgeBase.sql` | Page + nav inserts |
| `Import-Intranet-ItKnowledgeBase.ps1` | Runs SQL against LocalDocker or Production |
| `Import-Intranet-ItKnowledgeBase-Assets.csv` | Migration checklist + screenshot naming reference |

**Screenshot naming convention** (predictable filenames for Drive upload / Quill insert):

```
kb-{slug}-{step:02d}-{descriptor}.png     step-by-step (01, 02, …)
kb-{slug}-hero-{descriptor}.png           optional banner (no step number)
kb-{slug}-thumb.png                       optional card thumbnail
```

- `{slug}` = intranet page slug (`bitlocker`, `outlook-migration`, etc.)
- `{step}` = two-digit order as steps appear in the article
- `{descriptor}` = short kebab-case hint (`download-client`, `add-account`)
- Prefer `.png`; `.jpg` / `.webp` are fine if the source is not PNG

**Examples:** `kb-openvpn-01-download-client.png`, `kb-outlook-calendars-02-add-shared-calendar.png`

```powershell
# Local Docker (sign in once at https://localhost:7047 so your user exists)
.\Scripts\Import-Intranet-ItKnowledgeBase.ps1 -Profile LocalDocker

# Production
.\Scripts\Import-Intranet-ItKnowledgeBase.ps1 -Profile Production -CreatedByEmail you@example.com
```

After seeding: migrate HTML from Google Sites in `/intranet/editor`, upload screenshots to the
company Drive folder, publish each page when ready (`IsPublished=1`).

**Intranet menu nesting limit:** Admin → App Settings → **Intranet** tab → **Maximum menu nesting depth**
(controls levels deep, not total item count). DB key: `IntranetNavigationMaxDepth`. If missing on an
older database, run `Ensure-IntranetNavigationMaxDepth.sql` or apply EF migration
`20260707145432_AddIntranetNavigationMaxDepth`.

**Bulk content import** (optional — paste HTML into files first):

| File / folder | Purpose |
|---------------|---------|
| `Import-Intranet-ItKnowledgeBase-content/` | `{slug}.html` files (e.g. `openvpn.html`) |
| `Import-Intranet-PageContent.ps1` | Writes HTML to `IntranetPages.ContentMarkdown` by slug |

```powershell
.\Scripts\Import-Intranet-PageContent.ps1 -Slug openvpn          # one page
.\Scripts\Import-Intranet-PageContent.ps1                        # all .html in content folder
.\Scripts\Import-Intranet-PageContent.ps1 -Slug openvpn -Publish # import + publish
```