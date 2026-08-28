# Scripts

Helper scripts grouped by prefix:

| Prefix | Group | Scripts |
|--------|-------|---------|
| `Setup-` | First-time bootstrap | Local config, Azure resource provision |
| `Dev-` | Local development environment | Start full debug session, function host only, Docker SQL setup |
| `EfCore-` | EF Core **schema** migrations | Add migration, apply migrations to local DB |

EF Core migration **code** lives in `My.DAL/Data/Migrations/` — not in this folder. My.Workspace is greenfield-oriented: bake schema into `InitialMigration` for new installs.

## Setup- (first-time)

| Script | Purpose |
|--------|---------|
| `Setup-Local.ps1` | **Start here for local.** Creates `local.settings.json`, generates token encryption key, writes Google Client ID into API + client settings. |
| `Setup-Azure.ps1` | **Production.** Creates resource group, storage, Function App, Static Web App, App Insights, optional SQL. Use `-WhatIf` first. |

```powershell
.\Scripts\Setup-Local.ps1
.\Scripts\Setup-Local.ps1 -GoogleClientId "....apps.googleusercontent.com" -GoogleClientSecret "GOCSPX-..."

.\Scripts\Setup-Azure.ps1 -WhatIf
.\Scripts\Setup-Azure.ps1 -NamePrefix my-workspace -Location eastus
```

Docs: [SETUP-FROM-SCRATCH.md](../docs/SETUP-FROM-SCRATCH.md), [SETUP-AZURE.md](../docs/SETUP-AZURE.md).

## Dev- (local development)

| Script | Purpose |
|--------|---------|
| `Dev-StartDebugSession.ps1` / `.cmd` | **Preferred.** Docker SQL, Azurite, Functions (7074), Blazor client (7047 via shared boot helper), VS debugger attach. `-ClientFullReset` for stubborn WASM boot errors. |
| `Dev-StartFunctionHost.ps1` / `.cmd` | Functions API only — calls `Dev-SetupDockerSql.ps1`, then `func start` on 7074. |
| `Dev-StartClient.ps1` / `.cmd` | **Client only** — restart `dotnet watch` on 7047 (kills stale port, cleans output by default). Use when the full session is already up. |
| `Dev-SetupDockerSql.ps1` | SQL Server Docker container + updates `local.settings.json`. Called by the Dev-Start* scripts. |
| `Dev-TestCalendarWebhook.ps1` | POST a fake Google Calendar push to local Functions (enqueue / queue trigger smoke test). |

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
the hash of empty → integrity mismatch → boot hangs at 99%**. Debug builds now set
`<WasmFingerprintAssets>false</WasmFingerprintAssets>` so filenames are stable. Release keeps
fingerprinting for prod cache-busting.

If you still see a hang after that fix: hard-refresh (Ctrl+Shift+R), or run
`Dev-StartClient.ps1 -FullReset` / `Dev-StartDebugSession.ps1 -ClientFullReset`.

## EfCore- (schema migrations)

| Script | Purpose |
|--------|---------|
| `EfCore-AddMigration.ps1` | `dotnet ef migrations add` against My.DAL / My.AzureFunction |
| `EfCore-ApplyMigrations.ps1` | Apply pending migrations to the local DB |

For My.Workspace, prefer baking new columns/tables into **InitialMigration** when the change is meant for first-time installers. Keep later migration files empty or history-only when the column already exists in Initial.

## Intranet navigation depth

**Intranet menu nesting limit:** Admin → App Settings → **Intranet** tab → **Maximum menu nesting depth**
(controls levels deep, not total item count). DB key: `IntranetNavigationMaxDepth`. On greenfield
installs this is seeded by InitialMigration. For a database that is missing the setting, run
`Ensure-IntranetNavigationMaxDepth.sql`.
