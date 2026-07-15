# Google Cloud setup

My.Workspace signs users in with **Google OIDC** (OpenID Connect). Calendar and Drive are optional extras using the same OAuth client.

## 1. Create a Google Cloud project

1. Go to [Google Cloud Console](https://console.cloud.google.com/).
2. Create a project (or pick an existing one).
3. Note the project name — you’ll enable APIs on it later.

## 2. OAuth consent screen

1. **APIs & Services → OAuth consent screen**.
2. Choose **External** (personal Gmail) or **Internal** (Google Workspace only).
3. App name: e.g. `My Workspace`.
4. Support email: your email.
5. Add scopes at minimum: `openid`, `email`, `profile`.  
   For Calendar/Drive later: calendar and drive scopes as prompted by the app.
6. Add test users if the app is in Testing mode (External).

## 3. Create OAuth client ID

1. **Credentials → Create credentials → OAuth client ID**.
2. Application type: **Web application**.
3. Name: e.g. `My Workspace Web`.
4. **Authorized JavaScript origins** (examples):
   - `https://localhost:7047`
   - Your production URL (e.g. `https://your-app.azurestaticapps.net`)
5. **Authorized redirect URIs** (required):
   - `{origin}/authentication/login-callback` — sign-in
   - `{origin}/settings` — Calendar/Drive connect callback  

   Local:

   - `https://localhost:7047/authentication/login-callback`
   - `https://localhost:7047/settings`

6. Create and copy:
   - **Client ID** → client `appsettings.json` + Function `Google__ClientId`
   - **Client secret** → Function `Google__ClientSecret` only (never in the WASM appsettings)

## 4. Optional APIs

In **APIs & Services → Library**, enable:

- [Google Calendar API](https://console.cloud.google.com/apis/library/calendar-json.googleapis.com) — Tyme sync  
- [Google Drive API](https://console.cloud.google.com/apis/library/drive.googleapis.com) — Intranet media  

Wait a few minutes after enabling.

## 5. Token encryption key

Generate a 32-byte key (base64) for storing Google refresh tokens at rest:

```powershell
[Convert]::ToBase64String([byte[]](1..32 | ForEach-Object { Get-Random -Maximum 256 }))
```

Put it in Function settings as `Google__TokenEncryptionKey`. Do not commit it.

## 6. Allowed email domains

Not a Google Cloud setting — configured in My.Workspace:

- Setup wizard step **Access**, or  
- Environment: `Auth__AllowedEmailDomains=example.com` (comma-separated, or `*` for any verified Google email)

The SPA may send Google’s `hd` parameter when **exactly one** domain is configured; the **server always enforces** the policy.

## Checklist

- [ ] Consent screen configured  
- [ ] Web client created  
- [ ] Redirect URIs for every environment you use  
- [ ] Client ID in client + API  
- [ ] Client secret + encryption key on API (if using Calendar/Drive)  
- [ ] Calendar/Drive APIs enabled (optional)  
