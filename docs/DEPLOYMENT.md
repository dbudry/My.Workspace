# Deployment

How code gets from feature branches through `development` to production on `master`,
what's deployed where, and how to recover when something goes sideways.

## Branch flow

```
feature/* ──PR──▶ development ──auto-PR──▶ master ──▶ Deploy + GitHub Release
                       │                         │
                       ▼                         ▼
                  Build & test only         Production deploy
```

- **`development`** — integration branch. Merges run build + test via
  `.github/workflows/pipeline.yml`, then auto-open a PR to `master` (if one is not
  already open and `development` is ahead). No production deploy.
- **`master`** — production branch. Merges run a **sequenced** deploy workflow:
  build + test → deploy → GitHub Release `v<Version>` (each step skipped if upstream fails).

## Versioning & releases

The release version is the `<Version>` tag in `My.Client/My.Client.csproj`
(semantic versioning: `MAJOR.MINOR.PATCH`).

Rules enforced by `validate-version` on PRs into `master`:

1. HEAD must have a `<Version>` tag.
2. HEAD's version must be **strictly greater** than master's (version-sort).
3. If `master` has no `<Version>` yet, the first promotion is allowed (bootstrap).

On push to `development`, the `[Check] Version vs Master` job in `pipeline.yml`
emits a **warning** (not a failure) if
`<Version>` is not ahead of `master` — so you find out before the auto-promotion PR
merges to `master`.

**Bump the version** before merging `development → master`:

1. Bump `<Version>` in `My.Client/My.Client.csproj` on a feature branch.
2. PR into `development` and merge (CI runs build + test).
3. CI auto-opens PR `development → master` (or leaves an existing one open). The version
   gate on that PR must pass before merge.
4. Merge into `master` — build + test runs first, then deploy, then GitHub Release
   `v<Version>` (only if each prior step succeeds).

Configure **branch protection** on `master` to require `[Validate] Version Bump (PR >> Master)`
and `[Validate] Build & Test` from `pipeline.yml` before merge.

## CI/CD workflow

Single workflow: **`.github/workflows/pipeline.yml`** (sidebar name: **Pipeline**).

Run titles use Site-style prefixes (`[Validate]`, `[Publish]`, `[Create Pull Request]`, etc.).

| Trigger | Jobs that run |
|---|---|
| PR → `development` | `[Validate] Build & Test` → `[Merge] Pull Request >> Development` (auto-merge on success) |
| PR → `staging` / `master` | `[Validate] Build & Test` |
| PR → `master` | `[Validate] Version Bump` + `[Validate] Build & Test` (manual merge) |
| Push → `development` | `[Validate] Build & Test` → `[Check] Version vs Master` (warn) → `[Create Pull Request] Development >> Master` |
| Push → `staging` | `[Validate] Build & Test` |
| Push → `master` (app paths changed) | `[Detect] Changed Paths` → `[Validate] Build & Test` → `[Publish] Master >> Azure` → `[Create Release] Master >> Version` |

Downstream jobs use `needs:` + `if: !failure() && !cancelled()` so a failed validation
blocks deploy, release, and auto-PR.

**Path filter on `master`:** deploy/release jobs run only when `My.Client/`, `My.AzureFunction/`,
`My.DAL/`, `My.Shared/`, or `.github/workflows/` change — doc-only merges to `master` skip deploy.

## Pipeline at a glance

A push or merge to `master` (with app path changes) runs the `[Publish]` and `[Create Release]`
jobs in `pipeline.yml`.

The job runs on `ubuntu-latest` and proceeds in six phases:

1. **Azure login + fast-fail validation.** OIDC login, then `az staticwebapp show` and
   `az functionapp show` to fail in <30 s if the resource names are wrong. Pulls the
   SWA deployment token at the same time.
2. **Build & publish.** `dotnet restore` → `dotnet build -c Release` → publish each
   project to `./published-app` (client) and `./published-api` (functions).
3. **Database migration.** `dotnet ef database update` against Azure SQL using the
   `Active Directory Default` auth mode — runs **before** the new code ships, so the
   API never sees an old schema.
4. **Deploy Functions.** `azure/functions-action@v1` uploads the published zip to
   `func-my-workspace`.
5. **Deploy Client.** `Azure/static-web-apps-deploy@v1` ships the WASM bundle. The
   order matters: Functions first so the new frontend never lands while the old
   backend is still serving — old frontend hitting new backend is the only allowable
   gap, and we minimize even that with the function start step below.
6. **Ensure Function App is started + smoke test.** `az functionapp start` is
   idempotent (no-op if running); the workflow then `curl`s
   `POST /api/users/provision` unauthenticated and expects HTTP **401** within five
   attempts. Anything else fails the deploy.

## Required GitHub secrets

The workflow uses these secrets — all in **Repository Settings → Secrets and variables → Actions**:

| Secret | What |
|---|---|
| `PRODUCTION_AZURE_CLIENT_ID` | App registration's client ID with federated credentials configured for this repo. |
| `PRODUCTION_AZURE_TENANT_ID` | Azure AD tenant ID. |
| `PRODUCTION_AZURE_SUBSCRIPTION_ID` | The subscription holding `rg-my-workspace`. |
| `PAT` | GitHub token with `repo` scope (or fine-grained: Pull requests read/write, Contents read). Used by `create-pr-development-to-master` to open the `development → master` promotion PR. |

The deployment token for the SWA isn't a long-lived secret — it's fetched at
deploy time via `az staticwebapp secrets list`. The federated-credential setup
on the GitHub Actions OIDC app needs **Contributor** on the SWA itself for that
to work.

## Required Function App settings

In Azure Portal → `func-my-workspace` → **Configuration → Application settings**:

| Setting | Notes |
|---|---|
| `AzureWebJobsStorage` | Storage connection string for the host. |
| `FUNCTIONS_WORKER_RUNTIME` | `dotnet-isolated`. |
| `DefaultConnection` | SQL connection string. Production uses `Authentication=Active Directory Default` so the Function App's managed identity is what authenticates against the DB. |
| `Google__ClientId` | Same OAuth client ID the WASM client uses; `AuthMiddleware` validates tokens against this audience. |
| `Google__ClientSecret` | OAuth client secret used by `GoogleCalendarService` to exchange auth codes for refresh tokens during the Calendar connect flow. |
| `Google__TokenEncryptionKey` | 32-byte AES-GCM key (base64) used by `GoogleTokenEncryptor` to protect Google refresh tokens at rest in `UserSettings`. |

> The Calendar OAuth **redirect URI** is not stored as an app setting — the WASM
> client computes it as `{origin}/settings` and posts it to the API along with the
> auth code. Whatever origin the user signs in from (`your-app.example.com`, the SWA
> default URL, `localhost:7047`, …) needs `…/settings` registered on the Google
> Cloud Console OAuth client.

## Azure resources

| Resource | Name | Notes |
|---|---|---|
| Resource group | `rg-my-workspace` | Holds everything below. |
| Static Web App | `swa-my-workspace` | Free tier; serves the WASM client and proxies `/api/*` to the Function App. |
| Function App | `func-my-workspace` | Consumption plan. The `KeepaliveFunction` timer fires every 5 min to avoid host cold starts and pings SQL connectivity; the daily `GoogleCalendarWatchRenewalFunction` keeps push channels alive. |
| Storage account | (auto-named) | Required by the Function host. |
| SQL Server / DB | `your-sql.database.windows.net` / `MyWorkspace` | Azure AD auth in production. |
| Application Insights | linked to the Function App | Function logs + telemetry. The keepalive function logs at Debug level — bump the `Logging:LogLevel:My.Functions.KeepaliveFunction` setting if you want to see the pings. |

The Function App's **System-Assigned Managed Identity** must be **On**
(Function App → Identity → System assigned) and is granted:

| Role / Permission | Scope | Why |
|---|---|---|
| `db_datareader` + `db_datawriter` | `MyWorkspace` SQL DB | Application data access. Production connection string uses `Authentication=Active Directory Default`, so it's the MI authenticating, not a SQL login. |
| `Monitoring Reader` | Application Insights resource | Required by the `/admin/logs` page (global Admin only). The page calls `api.applicationinsights.io` with a managed-identity bearer token; without this role the API returns 403 and the page shows a friendly setup message. Assign on the **App Insights resource itself**, not the underlying Log Analytics workspace. |

The GitHub Actions OIDC app is granted **Contributor** at the resource-group level
so it can deploy and start/stop the Function App.

## CORS / routing

In production the Static Web App proxies `/api/*` to the Function App, so the
browser only ever talks to the SWA origin. CORS isn't actually traversed in
production — the proxy hides it. The local-dev story is different: the WASM
client hits the Function App directly, and `CorsMiddleware` handles preflight.

## Rollback

The `database update` step is the one you can't easily undo. Two flavors of
rollback:

### Code-only rollback (no schema regression)

Revert the offending merge on `master`. The next push triggers a fresh
deploy with the older code; the schema stays current. This is the boring,
recommended path.

### Code + schema rollback

If the bad change included a destructive migration:

1. Roll the schema back manually:
   ```bash
   dotnet ef database update <PreviousMigrationName> \
     --project My.DAL --startup-project My.AzureFunction
   ```
   Use the migration's name from `My.DAL/Data/Migrations/` — the one immediately
   *before* the bad migration.
2. Revert the merge on `master` and let CI redeploy.

If the migration was destructive enough that you can't roll forward later
without data loss (e.g. dropped a column), capture a DB backup before applying
the rollback and re-import the dropped data after the next forward migration.

business data and re-imports") makes it useful as a recovery tool when staging
data needs to be re-loaded from the old DB.

## Pausing the Function App for destructive ops

The deploy workflow includes an explicit `az functionapp start` step because
**pausing the Function App is the standard precaution before destructive
database operations** (renames, large data shuffles, the legacy migration). If
you're doing one of those:

```bash
az functionapp stop --name func-my-workspace --resource-group rg-my-workspace
# do the dangerous thing
az functionapp start --name func-my-workspace --resource-group rg-my-workspace
```

The workflow's "Ensure Function App is Started" step makes sure it comes back
up after the deploy regardless of how it was left.

## Smoke test interpretation

The workflow's smoke test calls `POST /api/users/provision` unauthenticated and
expects HTTP **401**. Other status codes mean different things:

| Code | Means |
|---|---|
| 401 | ✅ Auth middleware constructed and rejected an unauthenticated request. App is healthy. |
| 400 | Auth middleware failed to construct — usually a missing `Google:ClientId` setting. |
| 5xx | Function App didn't start cleanly. Check the host logs in Application Insights. |
| 200 | Auth was bypassed somehow. Should never happen — investigate immediately. |

## Cost notes

The Consumption plan is well inside the free monthly grant for typical use
(1M executions, 400k GB-seconds). The keepalive timer adds ~8,640 executions/month
which is <1% of the grant.

### Keepalive SQL ping cost

`KeepaliveFunction` runs every **5 minutes** and issues a single
`Database.CanConnectAsync()` against Azure SQL (in addition to waking the
Function worker). Rough monthly volume: **~8,640 pings**.

| SQL SKU | Effect of the ping | Expected cost impact |
|---|---|---|
| **Provisioned** (DTU / vCore) — production `MyWorkspace` today | Negligible connection check; DB is already billed 24/7 | **≈ $0 extra** (fraction of a DTU-second per ping) |
| **Serverless** (auto-pause enabled) | Pings **prevent auto-pause**, so you pay minimum vCores continuously | Can be **material** (you lose the paused discount) — avoid if you rely on pause savings |
| **Serverless** (auto-pause disabled) | Same as provisioned for billing | Ping cost still negligible |

On the current provisioned Azure SQL setup, the SQL keep-warm path is effectively free.
Do **not** switch production to serverless-with-auto-pause if you want this keepalive
to keep the DB warm — the ping would fight the pause and erase the savings.

The biggest cost levers in practice are:

- **SQL DTUs / vCores** — pick the size based on observed query latency.
- **Application Insights** — sampling helps if you see ingestion costs creep.
- **Static Web App** — Free tier is fine until you outgrow its 100 GB/month
  bandwidth ceiling.

If you ever want to eliminate cold starts entirely, the upgrade path is the
**Premium plan with Always Ready instances** (~$13/mo for B1). The keepalive
timer becomes redundant for the Function host in that case (SQL ping may still
help connection-pool warmth on provisioned SQL).
