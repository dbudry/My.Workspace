# Azure production setup

Optional path once local setup works. Resource **names are yours** — the GitHub Actions workflow reads them from repository variables.

## Resources to create

| Resource | Suggested name pattern | Notes |
|----------|------------------------|--------|
| Resource group | `rg-my-workspace` | Holds everything |
| Static Web App | `swa-my-workspace` | Hosts Blazor WASM; proxies `/api/*` |
| Function App | `func-my-workspace` | Consumption plan, .NET isolated |
| Storage account | (any unique) | Required by Functions |
| Azure SQL | server + database | Prefer Azure AD + managed identity |
| Application Insights | linked to Function App | Optional admin logs page |

Exact names go into GitHub **Variables** (not secrets):

| Variable | Example |
|----------|---------|
| `AZURE_RESOURCE_GROUP` | `rg-my-workspace` |
| `AZURE_SWA_NAME` | `swa-my-workspace` |
| `AZURE_FUNCTION_APP_NAME` | `func-my-workspace` |

## Function App settings

| Setting | Notes |
|---------|--------|
| `AzureWebJobsStorage` | Storage connection string |
| `FUNCTIONS_WORKER_RUNTIME` | `dotnet-isolated` |
| `DefaultConnection` | SQL connection string (often `Authentication=Active Directory Default`) |
| `Google__ClientId` | Same OAuth client as the SPA |
| `Google__ClientSecret` | OAuth secret |
| `Google__TokenEncryptionKey` | 32-byte base64 AES key |
| `Auth__AllowedEmailDomains` | e.g. `example.com` or `*` |

Register production redirect URIs on the Google OAuth client (`https://your-host/authentication/login-callback` and `.../settings`).

## GitHub Actions secrets

| Secret | Purpose |
|--------|---------|
| `PRODUCTION_AZURE_CLIENT_ID` | OIDC app registration for deploy |
| `PRODUCTION_AZURE_TENANT_ID` | Entra tenant |
| `PRODUCTION_AZURE_SUBSCRIPTION_ID` | Subscription with the resource group |
| `PAT` | Optional: open promotion PRs (if you use that flow) |

Configure federated credentials so Actions can log in without a client secret. Grant the identity rights to deploy SWA + Functions and run migrations against SQL as documented in [DEPLOYMENT.md](DEPLOYMENT.md).

## Client build

Production `appsettings.json` ships with the WASM bundle. Set `Authentication:Google:ClientId` before build/deploy (or inject via your preferred SWA config approach). `ApiBaseUrl` should remain relative `api/` for same-origin SWA proxy.

## First production admin

With empty database and migrations applied:

1. Ensure `Auth__AllowedEmailDomains` allows your admin email.  
2. Open the production URL → `/setup` if no users exist.  
3. Complete wizard / sign in — first user is Admin.

## Security notes

- Do not set `Auth__AllowedEmailDomains=*` on a public URL unless you understand that any Google account can attempt first-user bootstrap on an empty database.  
- After the first admin exists, unknown users receive 403 until invited.  
