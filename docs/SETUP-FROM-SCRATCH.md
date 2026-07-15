# Setup from scratch

This guide is for **self-hosters** (including non-developers who can follow a checklist).  
Goal: empty machine → running My.Workspace → first admin signed in.

## What you need

| Tool | Why |
|------|-----|
| [Git](https://git-scm.com/) | Clone the repo |
| [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) | Build client + API |
| [Azure Functions Core Tools v4](https://learn.microsoft.com/azure/azure-functions/functions-run-local) | Run the API locally |
| [Docker Desktop](https://www.docker.com/products/docker-desktop/) | SQL Server without installing SQL |
| A Google account | OAuth sign-in ([SETUP-GOOGLE-CLOUD.md](SETUP-GOOGLE-CLOUD.md)) |

Optional later: Azure subscription for production ([SETUP-AZURE.md](SETUP-AZURE.md)).

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

Copy your **Client ID** (and Client secret if you want Calendar/Drive).

## 3. Local config files

```powershell
copy My.AzureFunction\local.settings.example.json My.AzureFunction\local.settings.json
```

Edit **both**:

1. `My.AzureFunction/local.settings.json`  
   - `Google__ClientId` = your client ID  
   - `Google__ClientSecret` = client secret (optional for core app; needed for Calendar/Drive)  
   - `Google__TokenEncryptionKey` = 32 random bytes, base64 (e.g. from `[Convert]::ToBase64String((1..32 | ForEach-Object { Get-Random -Max 256 }) -as [byte[]])`)  
   - Leave `Auth__AllowedEmailDomains` empty — the setup wizard will set domains in the database  

2. `My.Client/wwwroot/appsettings.json`  
   - `Authentication:Google:ClientId` = **same** client ID  

`appsettings.Development.json` already points the client at `https://localhost:7074/api/`.

## 4. Start everything

```powershell
# Prefer: right-click Scripts\Dev-StartDebugSession.cmd → Run as administrator (first time)
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

## Troubleshooting

| Symptom | Fix |
|---------|-----|
| Setup says API not reachable | Ensure Functions host window is running; check `ApiBaseUrl` |
| Sign-in fails / 403 | Domain not allowed — re-open `/setup` before first user exists, or set `Auth__AllowedEmailDomains` in `local.settings.json` and restart API |
| Cert / CORS errors | Run elevated once: `dotnet dev-certs https --trust` |
| SQL never ready | Docker running? `docker ps`; reset container via script prompt (press R within 10s) |

## Production

See [SETUP-AZURE.md](SETUP-AZURE.md) and [DEPLOYMENT.md](DEPLOYMENT.md).
