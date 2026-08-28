# Setup from scratch

This guide is for **self-hosters** (including non-developers who can follow a checklist).  
Goal: empty machine → running My.Workspace → first admin signed in.

## Golden path (local)

| Step | What |
|------|------|
| 1 | Install prerequisites (below) |
| 2 | Create a Google OAuth Web client — [SETUP-GOOGLE-CLOUD.md](SETUP-GOOGLE-CLOUD.md) |
| 3 | `.\Scripts\Setup-Local.ps1` — writes local config + encryption key |
| 4 | `.\Scripts\Dev-StartDebugSession.ps1` — SQL, Azurite, API, client |
| 5 | Browser → `https://localhost:7047` → **`/setup`** wizard → first Admin |

Production later: [SETUP-AZURE.md](SETUP-AZURE.md) (`.\Scripts\Setup-Azure.ps1`).

## What you need

| Tool | Why |
|------|-----|
| [Git](https://git-scm.com/) | Clone the repo |
| [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) | Build client + API |
| [Azure Functions Core Tools v4](https://learn.microsoft.com/azure/azure-functions/functions-run-local) | Run the API locally |
| [Docker Desktop](https://www.docker.com/products/docker-desktop/) | SQL Server without installing SQL |
| A Google account | OAuth sign-in ([SETUP-GOOGLE-CLOUD.md](SETUP-GOOGLE-CLOUD.md)) |

Optional later: Azure subscription ([SETUP-AZURE.md](SETUP-AZURE.md)).

## 1. Clone

```powershell
git clone https://github.com/dbudry/My.Workspace.git
cd My.Workspace
```

## 2. Google Cloud (do this once)

Follow **[SETUP-GOOGLE-CLOUD.md](SETUP-GOOGLE-CLOUD.md)** and create a Web OAuth client.

Local redirect URIs to register:

- `https://localhost:7047/authentication/login-callback`
- `https://localhost:7047/settings`

Copy your **Client ID** and **Client secret** (secret required for Calendar/Drive).

## 3. Local config (one script)

```powershell
.\Scripts\Setup-Local.ps1
# or non-interactive:
.\Scripts\Setup-Local.ps1 -GoogleClientId "YOUR_ID.apps.googleusercontent.com" -GoogleClientSecret "GOCSPX-..."
```

This will:

- Create `My.AzureFunction/local.settings.json` from the example (if missing)
- Generate `Google__TokenEncryptionKey`
- Write the Client ID into both the API and `My.Client/wwwroot/appsettings.json`

Leave `Auth__AllowedEmailDomains` empty — the **setup wizard** sets domains in the database.

Never commit `local.settings.json` or real secrets.

## 4. Start everything

```powershell
# Prefer first time: right-click Scripts\Dev-StartDebugSession.cmd → Run as administrator
# (admin is for trusting the HTTPS dev certificate, not for Azure)
.\Scripts\Dev-StartDebugSession.ps1
```

This will:

- Start Docker SQL (`my-workspace-mssql`, database `MyWorkspace_Dev`)
- Update the API connection string
- Start Azurite, the Functions host (7074), and the Blazor client (7047)

First SQL container boot can take **5–12 minutes**. Watch progress with:

```powershell
docker logs my-workspace-mssql -f
```

## 5. Setup wizard

1. Open `https://localhost:7047` (browser may warn about the dev certificate once).
2. You should land on **`/setup`**.
3. Walk through:
   - Environment checks (API + DB)
   - Google Cloud checklist (redirect URIs auto-shown)
   - Access policy (your email domain, e.g. `gmail.com` or `example.com`, or `*` carefully)
   - **Sign in with Google** as the first admin

After the first successful sign-in, setup is complete. Later users must be created by an admin under **Admin → Users**.

## 6. First-day tasks

- Create a project (Tyme) and log a test entry  
- Optional: Settings → connect Google Calendar  
- Optional: Intranet → create a home page and nav item  
- Invite teammates with matching email domains  

### Google Calendar (optional)

Calendar import uses the Function App storage account (local: **Azurite**). The app creates the `google-calendar-import` queue and lock container automatically — no extra setup. Enable the **Google Calendar API** in Cloud Console, then connect under Settings. Smoke-test locally with `.\Scripts\Dev-TestCalendarWebhook.ps1` when the Functions host is running.

## Troubleshooting

| Symptom | Fix |
|---------|-----|
| Setup says API not reachable | Ensure Functions host window is running; check `ApiBaseUrl` |
| Sign-in fails / 403 | Domain not allowed — re-open `/setup` before first user exists, or set `Auth__AllowedEmailDomains` in `local.settings.json` and restart API |
| Cert / CORS errors | Run elevated once: `dotnet dev-certs https --trust` |
| SQL never ready | Docker running? `docker ps`; reset container via script prompt (press R within 10s) |
| Missing Client ID checks | Re-run `.\Scripts\Setup-Local.ps1 -GoogleClientId "..." -Force` |

## Production

See [SETUP-AZURE.md](SETUP-AZURE.md) (`Setup-Azure.ps1`) and [DEPLOYMENT.md](DEPLOYMENT.md).
