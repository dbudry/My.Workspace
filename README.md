# My.Workspace

Self-hosted workspace app: **time tracking (Tyme)**, **intranet**, and **Google Calendar/Drive** integration.

Built with Blazor WebAssembly, Azure Functions (.NET 10), EF Core, and Google OIDC.

If My.Workspace helps you, you can [buy me a coffee](https://www.buymeacoffee.com/dbudry).

```
Browser  ──►  Static Web App (Blazor WASM)  ──►  Azure Functions (HTTP)  ──►  Azure SQL
   │                                                   ▲
   └─────────────────────────────────────── Google OIDC ┘
```

## Start here (from scratch)

1. **[docs/SETUP-FROM-SCRATCH.md](docs/SETUP-FROM-SCRATCH.md)** — golden path (prereqs → local → `/setup`)  
2. **[docs/SETUP-GOOGLE-CLOUD.md](docs/SETUP-GOOGLE-CLOUD.md)** — OAuth client, redirect URIs, optional Calendar/Drive APIs  
3. `.\Scripts\Setup-Local.ps1` then `.\Scripts\Dev-StartDebugSession.ps1` → **Setup wizard** at `/setup`  
4. Optional production: **[docs/SETUP-AZURE.md](docs/SETUP-AZURE.md)** (`.\Scripts\Setup-Azure.ps1`)

## Quick local start

```powershell
# Prerequisites: .NET 10 SDK, Azure Functions Core Tools v4, Docker Desktop
git clone https://github.com/dbudry/My.Workspace.git
cd My.Workspace

# After creating a Google OAuth Web client (docs/SETUP-GOOGLE-CLOUD.md):
.\Scripts\Setup-Local.ps1 -GoogleClientId "YOUR_ID.apps.googleusercontent.com" -GoogleClientSecret "GOCSPX-..."

.\Scripts\Dev-StartDebugSession.ps1
```

Browse to `https://localhost:7047`, complete the setup wizard, then sign in with Google.
The **first** user becomes global Admin (+ Admin:Tyme, Admin:Intranet). Later users must be invited.

## Highlights

- **Auth**: Google OIDC; allowed email domains are **configurable** (setup wizard or `Auth__AllowedEmailDomains`)
- **Scoped roles**: `Admin` / `Manager` / `User` (and Editor), optionally scoped (`Admin:Tyme`, `Editor:Intranet`)
- **Tyme**: stopwatch, calendar, reports, monthly submission, manager corrections
- **Intranet**: hierarchical pages, Quill editor, Drive attachments, favorites
- **Google Calendar** two-way sync (optional) with team availability calendar support

## Repository layout

```
My.Workspace/
├── My.Client/          Blazor WASM SPA
├── My.AzureFunction/   Isolated-worker API
├── My.DAL/             EF Core + migrations
├── My.Shared/          DTOs, rules, constants
├── My.Tests/           Unit / integration tests
├── Scripts/            Dev- and EfCore- helpers
└── docs/               Setup, architecture, deployment
```

## Configuration (essentials)

| Where | Keys |
|-------|------|
| `My.Client/wwwroot/appsettings.json` | `Authentication:Google:ClientId`, optional `Auth:AllowedEmailDomains` for OIDC `hd` hint |
| `My.AzureFunction/local.settings.json` | `Google__ClientId`, `Google__ClientSecret`, `Google__TokenEncryptionKey`, `Auth__AllowedEmailDomains`, `ConnectionStrings:DefaultConnection` |

See setup docs for full tables. Never commit real secrets.

## Deeper docs

| Doc | Contents |
|-----|----------|
| [SETUP-FROM-SCRATCH.md](docs/SETUP-FROM-SCRATCH.md) | Primary self-host path (local) |
| [SETUP-GOOGLE-CLOUD.md](docs/SETUP-GOOGLE-CLOUD.md) | Google OAuth |
| [SETUP-AZURE.md](docs/SETUP-AZURE.md) | Azure provision script + remaining checklist |
| [DEVELOPMENT.md](docs/DEVELOPMENT.md) | Day-to-day development |
| [ARCHITECTURE.md](docs/ARCHITECTURE.md) | How the pieces fit |
| [DEPLOYMENT.md](docs/DEPLOYMENT.md) | CI/CD branch flow |

## License

MIT — Copyright (c) 2026 Derek Budry
