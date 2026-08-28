<#
.SYNOPSIS
    One-time local config bootstrap for My.Workspace.

.DESCRIPTION
    Makes first-run setup easy:
    - Copies local.settings.example.json → local.settings.json if missing
    - Generates Google__TokenEncryptionKey when still a placeholder
    - Writes Google Client ID (and optional secret) into Function + client settings

    After this script, run Dev-StartDebugSession.ps1 and complete the in-app /setup wizard.

.PARAMETER GoogleClientId
    OAuth Web client ID from Google Cloud Console. If omitted, you are prompted when needed.

.PARAMETER GoogleClientSecret
    OAuth client secret (needed for Calendar/Drive). If omitted, you may be prompted.

.PARAMETER SkipGooglePrompt
    Do not prompt for Google credentials; only create/fix local.settings and encryption key.

.PARAMETER Force
    Overwrite an existing non-placeholder Client ID / secret.

.EXAMPLE
    .\Scripts\Setup-Local.ps1
    .\Scripts\Setup-Local.ps1 -GoogleClientId "123.apps.googleusercontent.com" -GoogleClientSecret "GOCSPX-..."
#>
[CmdletBinding()]
param(
    [string]$GoogleClientId = "",
    [string]$GoogleClientSecret = "",
    [switch]$SkipGooglePrompt,
    [switch]$Force
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
if (-not (Test-Path (Join-Path $root 'My.AzureFunction'))) {
    $root = $PSScriptRoot
    if (-not (Test-Path (Join-Path $root 'My.AzureFunction'))) {
        throw "Run this script from the My.Workspace repo (Scripts\\Setup-Local.ps1)."
    }
}

$examplePath = Join-Path $root 'My.AzureFunction\local.settings.example.json'
$localPath = Join-Path $root 'My.AzureFunction\local.settings.json'
$clientAppsettings = Join-Path $root 'My.Client\wwwroot\appsettings.json'

function Test-IsPlaceholder([string]$value) {
    if ([string]::IsNullOrWhiteSpace($value)) { return $true }
    $v = $value.Trim()
    return ($v -match 'YOUR_|placeholder|changeme|example\.com' -or $v -eq 'YOUR_32_BYTE_BASE64_KEY')
}

function New-TokenEncryptionKey {
    $bytes = New-Object byte[] 32
    [System.Security.Cryptography.RandomNumberGenerator]::Fill($bytes)
    return [Convert]::ToBase64String($bytes)
}

function Read-Secret([string]$prompt) {
    $secure = Read-Host -Prompt $prompt -AsSecureString
    $bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
    try {
        return [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr)
    }
    finally {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr)
    }
}

Write-Host ""
Write-Host "My.Workspace — local setup" -ForegroundColor Cyan
Write-Host "Repo: $root" -ForegroundColor DarkGray
Write-Host ""

# --- local.settings.json ---
if (-not (Test-Path $examplePath)) {
    throw "Missing $examplePath"
}

$createdLocal = $false
if (-not (Test-Path $localPath)) {
    Copy-Item -Path $examplePath -Destination $localPath
    $createdLocal = $true
    Write-Host "[ok] Created My.AzureFunction/local.settings.json from example" -ForegroundColor Green
}
else {
    Write-Host "[ok] local.settings.json already exists" -ForegroundColor Green
}

$local = Get-Content $localPath -Raw | ConvertFrom-Json
if (-not $local.Values) {
    throw "local.settings.json has no Values object."
}

# Token encryption key
$currentKey = [string]$local.Values.Google__TokenEncryptionKey
if ($Force -or (Test-IsPlaceholder $currentKey)) {
    $newKey = New-TokenEncryptionKey
    $local.Values.Google__TokenEncryptionKey = $newKey
    Write-Host "[ok] Generated Google__TokenEncryptionKey" -ForegroundColor Green
}
else {
    Write-Host "[ok] Google__TokenEncryptionKey already set (use -Force to rotate)" -ForegroundColor Green
}

# Google credentials
$needClientId = $Force -or (Test-IsPlaceholder ([string]$local.Values.Google__ClientId))
$needSecret = $Force -or (Test-IsPlaceholder ([string]$local.Values.Google__ClientSecret))

if (-not $SkipGooglePrompt) {
    if ([string]::IsNullOrWhiteSpace($GoogleClientId) -and $needClientId) {
        Write-Host ""
        Write-Host "Create an OAuth Web client first (docs/SETUP-GOOGLE-CLOUD.md), then paste values here." -ForegroundColor Yellow
        $GoogleClientId = Read-Host "Google Client ID (Enter to skip)"
    }
    if ([string]::IsNullOrWhiteSpace($GoogleClientSecret) -and $needSecret -and -not [string]::IsNullOrWhiteSpace($GoogleClientId)) {
        $GoogleClientSecret = Read-Secret "Google Client Secret (Calendar/Drive; Enter blank to skip)"
    }
}

if (-not [string]::IsNullOrWhiteSpace($GoogleClientId)) {
    if ($needClientId -or $Force) {
        $local.Values.Google__ClientId = $GoogleClientId.Trim()
        Write-Host "[ok] Wrote Google__ClientId to local.settings.json" -ForegroundColor Green
    }
    else {
        Write-Host "[skip] Google__ClientId already set (use -Force to overwrite)" -ForegroundColor DarkYellow
    }
}
elseif ($needClientId) {
    Write-Host "[todo] Set Google__ClientId (re-run with -GoogleClientId or edit local.settings.json)" -ForegroundColor Yellow
}

if (-not [string]::IsNullOrWhiteSpace($GoogleClientSecret)) {
    if ($needSecret -or $Force) {
        $local.Values.Google__ClientSecret = $GoogleClientSecret.Trim()
        Write-Host "[ok] Wrote Google__ClientSecret to local.settings.json" -ForegroundColor Green
    }
    else {
        Write-Host "[skip] Google__ClientSecret already set (use -Force to overwrite)" -ForegroundColor DarkYellow
    }
}
elseif ($needSecret) {
    Write-Host "[todo] Set Google__ClientSecret for Calendar/Drive (optional for sign-in only)" -ForegroundColor Yellow
}

# Leave Auth__AllowedEmailDomains empty — /setup wizard sets domains in the DB
$json = $local | ConvertTo-Json -Depth 8
# PowerShell ConvertTo-Json can reorder; fine for local settings
Set-Content -Path $localPath -Value $json -Encoding utf8
Write-Host "[ok] Saved $localPath" -ForegroundColor Green

# --- client appsettings.json ---
if (-not (Test-Path $clientAppsettings)) {
    throw "Missing $clientAppsettings"
}

$clientRaw = Get-Content $clientAppsettings -Raw
$client = $clientRaw | ConvertFrom-Json
$clientIdNow = ""
if ($client.Authentication -and $client.Authentication.Google) {
    $clientIdNow = [string]$client.Authentication.Google.ClientId
}

$effectiveClientId = $GoogleClientId
if ([string]::IsNullOrWhiteSpace($effectiveClientId)) {
    $effectiveClientId = [string]$local.Values.Google__ClientId
}
$effectiveClientId = $effectiveClientId.Trim()

if (-not (Test-IsPlaceholder $effectiveClientId)) {
    $shouldWriteClient = $Force -or (Test-IsPlaceholder $clientIdNow)
    if ($shouldWriteClient) {
        # Preserve formatting: replace the ClientId string value in-place when present.
        if ($clientRaw -match '"ClientId"\s*:\s*"[^"]*"') {
            $updated = [regex]::Replace(
                $clientRaw,
                '("Authentication"\s*:\s*\{[^{}]*"Google"\s*:\s*\{[^{}]*"ClientId"\s*:\s*")[^"]*(")',
                "`${1}$effectiveClientId`${2}",
                1
            )
            if ($updated -eq $clientRaw) {
                $updated = [regex]::Replace($clientRaw, '("ClientId"\s*:\s*")[^"]*(")', "`${1}$effectiveClientId`${2}", 1)
            }
            Set-Content -Path $clientAppsettings -Value $updated -Encoding utf8 -NoNewline
        }
        else {
            $client.Authentication.Google.ClientId = $effectiveClientId
            Set-Content -Path $clientAppsettings -Value ($client | ConvertTo-Json -Depth 8) -Encoding utf8
        }
        Write-Host "[ok] Wrote Authentication:Google:ClientId to client appsettings.json" -ForegroundColor Green
    }
    else {
        Write-Host "[ok] Client appsettings ClientId already set (use -Force to overwrite)" -ForegroundColor Green
    }
}
else {
    Write-Host "[todo] Set Authentication:Google:ClientId in My.Client/wwwroot/appsettings.json" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "Next steps" -ForegroundColor Cyan
Write-Host "  1. Google Cloud Console — authorized redirect URIs:" -ForegroundColor White
Write-Host "       https://localhost:7047/authentication/login-callback" -ForegroundColor DarkGray
Write-Host "       https://localhost:7047/settings" -ForegroundColor DarkGray
Write-Host "     (full guide: docs/SETUP-GOOGLE-CLOUD.md)" -ForegroundColor DarkGray
Write-Host "  2. Start the stack (admin recommended once for HTTPS cert trust):" -ForegroundColor White
Write-Host "       .\Scripts\Dev-StartDebugSession.ps1" -ForegroundColor Green
Write-Host "  3. Open https://localhost:7047 and finish the /setup wizard (first user = Admin)." -ForegroundColor White
Write-Host ""
Write-Host "Optional: enable Google Calendar API + Drive API, then Settings → connect Calendar." -ForegroundColor DarkGray
Write-Host "Never commit local.settings.json or real secrets." -ForegroundColor DarkYellow
Write-Host ""

