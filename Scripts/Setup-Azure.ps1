<#
.SYNOPSIS
    Provisions the core Azure resources for a My.Workspace production deploy.

.DESCRIPTION
    Uses Azure CLI (az) to create (idempotently where possible):
      - Resource group
      - Storage account (Functions host + calendar import queue/locks auto-created by the app)
      - Application Insights
      - Function App (.NET isolated, consumption)
      - Static Web App
      - Optional Azure SQL server + database

    Does NOT deploy code. Does NOT create Google OAuth clients or GitHub secrets.
    Prints the remaining checklist when finished.

.PARAMETER NamePrefix
    Short name used in resource names. Default: my-workspace

.PARAMETER Location
    Azure region. Default: eastus

.PARAMETER SubscriptionId
    Subscription to use. Default: current az account.

.PARAMETER SkipSql
    Skip Azure SQL creation (bring your own connection string later).

.PARAMETER SqlAdminUser
    SQL server admin login (ignored with -SkipSql). Default: myworkspaceadmin

.PARAMETER SqlAdminPassword
    SQL admin password. Prompted if omitted and SQL is created.

.PARAMETER GoogleClientId
    Optional. Written to Function App settings when provided.

.PARAMETER GoogleClientSecret
    Optional. Written to Function App settings when provided.

.EXAMPLE
    .\Scripts\Setup-Azure.ps1 -WhatIf
    .\Scripts\Setup-Azure.ps1 -NamePrefix my-workspace -Location eastus
    .\Scripts\Setup-Azure.ps1 -SkipSql -GoogleClientId "....apps.googleusercontent.com"
#>
[CmdletBinding(SupportsShouldProcess = $true)]
param(
    [string]$NamePrefix = "my-workspace",
    [string]$Location = "eastus",
    [string]$SubscriptionId = "",
    [switch]$SkipSql,
    [string]$SqlAdminUser = "myworkspaceadmin",
    [securestring]$SqlAdminPassword,
    [string]$GoogleClientId = "",
    [string]$GoogleClientSecret = ""
)

$ErrorActionPreference = 'Stop'

function Assert-AzCli {
    if (Get-Command az -ErrorAction SilentlyContinue) { return }
    if (Test-WhatIfMode) {
        Write-Host "[WhatIf] Azure CLI (az) not installed — plan only." -ForegroundColor DarkYellow
        return
    }
    throw "Azure CLI (az) not found. Install: https://learn.microsoft.com/cli/azure/install-azure-cli"
}

function Test-WhatIfMode {
    return [bool]$WhatIfPreference
}

function Invoke-Az([string[]]$AzArgs, [string]$Description) {
    if (Test-WhatIfMode) {
        Write-Host "[WhatIf] $Description" -ForegroundColor DarkYellow
        Write-Host "         az $($AzArgs -join ' ')" -ForegroundColor DarkGray
        return $null
    }
    Write-Host "→ $Description" -ForegroundColor Cyan
    $out = & az @AzArgs 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "az failed ($Description): $out"
    }
    return $out
}

function Get-AzJson([string[]]$AzArgs) {
    if (Test-WhatIfMode) { return $null }
    $raw = & az @AzArgs -o json 2>&1
    if ($LASTEXITCODE -ne 0) { return $null }
    if ([string]::IsNullOrWhiteSpace($raw)) { return $null }
    return $raw | ConvertFrom-Json
}

function New-TokenEncryptionKey {
    $bytes = New-Object byte[] 32
    [System.Security.Cryptography.RandomNumberGenerator]::Fill($bytes)
    return [Convert]::ToBase64String($bytes)
}

function ConvertFrom-SecureStringPlain([securestring]$Secure) {
    $bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($Secure)
    try { return [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr) }
    finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr) }
}

# --- names ---
$prefix = $NamePrefix.Trim().ToLowerInvariant() -replace '[^a-z0-9-]', '-'
if ([string]::IsNullOrWhiteSpace($prefix)) { throw "NamePrefix is required." }

$rg = "rg-$prefix"
$func = "func-$prefix"
$swa = "swa-$prefix"
$insights = "appi-$prefix"
# Storage: 3-24 lowercase alphanumeric only
$storageBase = ($prefix -replace '[^a-z0-9]', '')
if ($storageBase.Length -gt 18) { $storageBase = $storageBase.Substring(0, 18) }
$storage = "st${storageBase}mw"
if ($storage.Length -gt 24) { $storage = $storage.Substring(0, 24) }
$sqlServer = "sql-$prefix"
$sqlDb = "MyWorkspace"

Write-Host ""
Write-Host "My.Workspace — Azure setup" -ForegroundColor Cyan
Write-Host "Prefix: $prefix  Location: $Location" -ForegroundColor DarkGray
if (Test-WhatIfMode) { Write-Host "Mode: WhatIf (no changes)" -ForegroundColor DarkYellow }
Write-Host ""

Assert-AzCli

if (Test-WhatIfMode) {
    Write-Host "[WhatIf] Plan only — az login not required." -ForegroundColor DarkYellow
    if ($SubscriptionId) {
        Write-Host "[WhatIf] Would use subscription $SubscriptionId" -ForegroundColor DarkYellow
    }
}
else {
    $account = Get-AzJson @('account', 'show')
    if (-not $account) {
        throw "Not logged in. Run: az login"
    }
    if ($SubscriptionId) {
        Invoke-Az @('account', 'set', '--subscription', $SubscriptionId) "Set subscription $SubscriptionId" | Out-Null
        $account = Get-AzJson @('account', 'show')
    }
    Write-Host "[ok] Subscription: $($account.name) ($($account.id))" -ForegroundColor Green
}

function Ensure-Resource {
    param(
        [string]$ShowDescription,
        [string[]]$ShowArgs,
        [string]$CreateDescription,
        [string[]]$CreateArgs
    )
    if (Test-WhatIfMode) {
        Invoke-Az $CreateArgs $CreateDescription | Out-Null
        return
    }
    $existing = Get-AzJson $ShowArgs
    if ($existing) {
        Write-Host "[ok] $ShowDescription" -ForegroundColor Green
    }
    else {
        Invoke-Az $CreateArgs $CreateDescription | Out-Null
    }
}

Ensure-Resource `
    -ShowDescription "Resource group exists: $rg" `
    -ShowArgs @('group', 'show', '--name', $rg) `
    -CreateDescription "Create resource group $rg" `
    -CreateArgs @('group', 'create', '--name', $rg, '--location', $Location)

Ensure-Resource `
    -ShowDescription "Storage exists: $storage" `
    -ShowArgs @('storage', 'account', 'show', '--name', $storage, '--resource-group', $rg) `
    -CreateDescription "Create storage account $storage" `
    -CreateArgs @(
        'storage', 'account', 'create',
        '--name', $storage,
        '--resource-group', $rg,
        '--location', $Location,
        '--sku', 'Standard_LRS',
        '--kind', 'StorageV2',
        '--allow-blob-public-access', 'false'
    )

$storageConn = $null
if (-not (Test-WhatIfMode)) {
    $storageConn = (& az storage account show-connection-string --name $storage --resource-group $rg --query connectionString -o tsv).Trim()
    if ([string]::IsNullOrWhiteSpace($storageConn)) { throw "Could not read storage connection string." }
}

Ensure-Resource `
    -ShowDescription "Application Insights exists: $insights" `
    -ShowArgs @('monitor', 'app-insights', 'component', 'show', '--app', $insights, '--resource-group', $rg) `
    -CreateDescription "Create Application Insights $insights" `
    -CreateArgs @(
        'monitor', 'app-insights', 'component', 'create',
        '--app', $insights,
        '--location', $Location,
        '--resource-group', $rg,
        '--application-type', 'web',
        '--kind', 'web'
    )

$aiKey = $null
$aiConn = $null
if (-not (Test-WhatIfMode)) {
    $ai = Get-AzJson @('monitor', 'app-insights', 'component', 'show', '--app', $insights, '--resource-group', $rg)
    $aiKey = [string]$ai.instrumentationKey
    $aiConn = [string]$ai.connectionString
}

Ensure-Resource `
    -ShowDescription "Function App exists: $func" `
    -ShowArgs @('functionapp', 'show', '--name', $func, '--resource-group', $rg) `
    -CreateDescription "Create Function App $func" `
    -CreateArgs @(
        'functionapp', 'create',
        '--name', $func,
        '--resource-group', $rg,
        '--consumption-plan-location', $Location,
        '--runtime', 'dotnet-isolated',
        '--runtime-version', '8',
        '--functions-version', '4',
        '--storage-account', $storage,
        '--os-type', 'Windows'
    )
# runtime-version 8 is widely available for create; after deploy, set the app to your repo's .NET version if needed.

Invoke-Az @(
    'functionapp', 'identity', 'assign',
    '--name', $func,
    '--resource-group', $rg
) "Enable system-assigned managed identity on $func" | Out-Null

$tokenKey = New-TokenEncryptionKey
if (Test-WhatIfMode) {
    Write-Host "[WhatIf] Would set Function App settings (storage, insights, Google__TokenEncryptionKey, optional Google_*)." -ForegroundColor DarkYellow
}
else {
    $settings = @(
        "FUNCTIONS_WORKER_RUNTIME=dotnet-isolated",
        "AzureWebJobsStorage=$storageConn",
        "Google__TokenEncryptionKey=$tokenKey"
    )
    if ($aiConn) { $settings += "APPLICATIONINSIGHTS_CONNECTION_STRING=$aiConn" }
    elseif ($aiKey) { $settings += "APPINSIGHTS_INSTRUMENTATIONKEY=$aiKey" }
    if (-not [string]::IsNullOrWhiteSpace($GoogleClientId)) {
        $settings += "Google__ClientId=$($GoogleClientId.Trim())"
    }
    if (-not [string]::IsNullOrWhiteSpace($GoogleClientSecret)) {
        $settings += "Google__ClientSecret=$($GoogleClientSecret.Trim())"
    }
    $setArgs = @('functionapp', 'config', 'appsettings', 'set', '--name', $func, '--resource-group', $rg, '--settings') + $settings
    Invoke-Az $setArgs "Configure Function App settings" | Out-Null
    Write-Host "[ok] Wrote Google__TokenEncryptionKey (visible in portal App settings — not printed here)." -ForegroundColor Green
    if ([string]::IsNullOrWhiteSpace($GoogleClientId)) {
        Write-Host "[todo] Set Google__ClientId / Google__ClientSecret on the Function App when ready." -ForegroundColor Yellow
    }
}

Ensure-Resource `
    -ShowDescription "Static Web App exists: $swa" `
    -ShowArgs @('staticwebapp', 'show', '--name', $swa, '--resource-group', $rg) `
    -CreateDescription "Create Static Web App $swa" `
    -CreateArgs @(
        'staticwebapp', 'create',
        '--name', $swa,
        '--resource-group', $rg,
        '--location', $Location,
        '--sku', 'Free'
    )

$swaHostname = $null
if (-not (Test-WhatIfMode)) {
    $swaObj = Get-AzJson @('staticwebapp', 'show', '--name', $swa, '--resource-group', $rg)
    $swaHostname = [string]$swaObj.defaultHostname
}

$sqlConnHint = "Server=YOUR.sql.database.windows.net;Database=$sqlDb;Authentication=Active Directory Default;"
if ($SkipSql) {
    Write-Host "[ok] Skipping Azure SQL (-SkipSql). Set DefaultConnection yourself later." -ForegroundColor Green
}
else {
    if (-not $SqlAdminPassword -and -not (Test-WhatIfMode)) {
        $SqlAdminPassword = Read-Host "SQL admin password for $sqlServer" -AsSecureString
    }
    $plainSqlPw = if ($SqlAdminPassword) { ConvertFrom-SecureStringPlain $SqlAdminPassword } else { "WhatIfP@ssw0rd!" }

    Ensure-Resource `
        -ShowDescription "SQL server exists: $sqlServer" `
        -ShowArgs @('sql', 'server', 'show', '--name', $sqlServer, '--resource-group', $rg) `
        -CreateDescription "Create SQL server $sqlServer" `
        -CreateArgs @(
            'sql', 'server', 'create',
            '--name', $sqlServer,
            '--resource-group', $rg,
            '--location', $Location,
            '--admin-user', $SqlAdminUser,
            '--admin-password', $plainSqlPw
        )

    Ensure-Resource `
        -ShowDescription "SQL database exists: $sqlDb" `
        -ShowArgs @('sql', 'db', 'show', '--server', $sqlServer, '--name', $sqlDb, '--resource-group', $rg) `
        -CreateDescription "Create SQL database $sqlDb" `
        -CreateArgs @(
            'sql', 'db', 'create',
            '--server', $sqlServer,
            '--resource-group', $rg,
            '--name', $sqlDb,
            '--service-objective', 'Basic',
            '--backup-storage-redundancy', 'Local'
        )

    Ensure-Resource `
        -ShowDescription "SQL firewall AllowAzureServices exists" `
        -ShowArgs @('sql', 'server', 'firewall-rule', 'show', '--resource-group', $rg, '--server', $sqlServer, '--name', 'AllowAzureServices') `
        -CreateDescription "Allow Azure services to reach SQL" `
        -CreateArgs @(
            'sql', 'server', 'firewall-rule', 'create',
            '--resource-group', $rg,
            '--server', $sqlServer,
            '--name', 'AllowAzureServices',
            '--start-ip-address', '0.0.0.0',
            '--end-ip-address', '0.0.0.0'
        )

    $sqlFqdn = "$sqlServer.database.windows.net"
    $sqlConnHint = "Server=$sqlFqdn;Database=$sqlDb;Authentication=Active Directory Default;"

    if (-not (Test-WhatIfMode)) {
        Invoke-Az @(
            'functionapp', 'config', 'appsettings', 'set',
            '--name', $func,
            '--resource-group', $rg,
            '--settings', "DefaultConnection=$sqlConnHint", "ConnectionStrings__DefaultConnection=$sqlConnHint"
        ) "Set DefaultConnection on Function App" | Out-Null
        Write-Host "[todo] Grant the Function App managed identity db_datareader + db_datawriter on $sqlDb (see docs/SETUP-AZURE.md)." -ForegroundColor Yellow
    }
}

$funcHost = if (Test-WhatIfMode) { "$func.azurewebsites.net" } else {
    $f = Get-AzJson @('functionapp', 'show', '--name', $func, '--resource-group', $rg)
    [string]$f.defaultHostName
}

Write-Host ""
Write-Host "Created / verified" -ForegroundColor Cyan
Write-Host "  Resource group : $rg"
Write-Host "  Storage        : $storage"
Write-Host "  Insights       : $insights"
Write-Host "  Function App   : $func  ($funcHost)"
Write-Host "  Static Web App : $swa  $(if ($swaHostname) { $swaHostname } else { '(hostname after create)' })"
if (-not $SkipSql) { Write-Host "  SQL            : $sqlServer / $sqlDb" }

Write-Host ""
Write-Host "Calendar note" -ForegroundColor Cyan
Write-Host "  Import queue 'google-calendar-import' and lock container 'google-calendar-import-locks'"
Write-Host "  are created automatically by the app on first use. No manual queue setup."

Write-Host ""
Write-Host "Remaining checklist" -ForegroundColor Cyan
Write-Host "  1. Google Cloud — add production redirect URIs for your SWA (and Function host if used):"
if ($swaHostname) {
    Write-Host "       https://$swaHostname/authentication/login-callback"
    Write-Host "       https://$swaHostname/settings"
}
else {
    Write-Host "       https://<swa-host>/authentication/login-callback"
    Write-Host "       https://<swa-host>/settings"
}
Write-Host "     Enable Calendar API / Drive API if you need those features."
Write-Host "  2. Function App settings — ensure Google__ClientId, Google__ClientSecret, DefaultConnection."
Write-Host "  3. Link SWA → Function API proxy (Azure Static Web Apps → APIs) or your preferred reverse proxy."
Write-Host "  4. Deploy client + Functions (your pipeline or zip deploy). See docs/DEPLOYMENT.md."
Write-Host "  5. Open the site → /setup → first Google sign-in becomes Admin."
Write-Host "  6. Optional: Settings → connect Google Calendar; Admin → Debug → Google Calendar."

Write-Host ""
Write-Host "Suggested GitHub Variables (if you wire CI deploy)" -ForegroundColor Cyan
Write-Host "  AZURE_RESOURCE_GROUP=$rg"
Write-Host "  AZURE_SWA_NAME=$swa"
Write-Host "  AZURE_FUNCTION_APP_NAME=$func"
Write-Host ""
Write-Host "Done. Full write-up: docs/SETUP-AZURE.md" -ForegroundColor Green
Write-Host ""

