<#
.SYNOPSIS
    Quick helper to start the Azure Functions host (My.AzureFunction) for local development.

.DESCRIPTION
    - Ensures SQL Server is running in Docker (pulls image / creates / starts container as needed)
    - Starts Azurite (storage emulator) in a separate window if it's not already running
    - Changes to the function app folder and runs `func start --port 7074 --useHttps` (HTTPS by default)

    This version is designed to work reliably when launched from Visual Studio
    (Solution Explorer "Open Command Line", "Execute File", or integrated terminal),
    as well as from double-click or manual terminal use.
    It always determines its own location using $PSScriptRoot / $MyInvocation
    instead of relying on the caller's current working directory.

    HTTPS is enabled by default so the host matches the Blazor client's
    https://localhost:7074/api/ configuration.

.USAGE
    From the solution root:
        .\scripts\Dev-StartFunctionHost.ps1

    Or double-click Dev-StartFunctionHost.cmd (preferred for one-click use).

    Defaults to port 7074 + HTTPS (to match client https://localhost:7074/api/).

    IMPORTANT: Creating the development certificate usually requires Administrator rights.
    → Right-click Dev-StartFunctionHost.cmd and select "Run as administrator".

    Custom port:
        .\scripts\Dev-StartFunctionHost.ps1 -Port 7074

    Skip Azurite:
        .\scripts\Dev-StartFunctionHost.ps1 -NoAzurite

    Disable HTTPS (use HTTP instead):
        .\scripts\Dev-StartFunctionHost.ps1 -Port 7074 -UseHttps:$false
#>

[CmdletBinding()]
param(
    [int]$Port = 7074,
    [switch]$NoAzurite,
    [switch]$UseHttps
)

# Default to HTTPS so that Dev-StartFunctionHost.cmd (and direct .ps1 calls)
# start the Functions host on https://localhost:7074 to match the client's
# appsettings.Development.json (and .Debug.json) configuration.
if (-not $PSBoundParameters.ContainsKey('UseHttps')) {
    $UseHttps = [switch]$true
}

$ErrorActionPreference = 'Stop'

function Test-IsAdministrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

# === Robust directory handling for VS / any caller cwd ===
# $PSScriptRoot is the directory containing this .ps1 (works in PS 3+ when invoked via -File).
# Fall back to $MyInvocation for older hosts or edge cases.
$scriptRoot = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Path }

# Change to the script's own directory immediately.
# This makes the rest of the script (and any relative paths) independent of
# whatever directory Visual Studio or the caller happened to set.
Set-Location $scriptRoot

Write-Host "=== Starting My.Workspace Functions Host ===" -ForegroundColor Cyan
Write-Host "Script location (self-detected): $scriptRoot" -ForegroundColor DarkGray

$root = Split-Path -Parent $scriptRoot          # 
$funcAppDir = Join-Path $root "My.AzureFunction"

Write-Host "Project root : $root" -ForegroundColor DarkGray
Write-Host "Function app : $funcAppDir" -ForegroundColor DarkGray
Write-Host "Target port  : $Port" -ForegroundColor DarkGray
if ($UseHttps) {
    Write-Host "HTTPS      : Enabled (--useHttps)" -ForegroundColor DarkGray
} else {
    Write-Host "HTTPS      : Disabled (plain HTTP)" -ForegroundColor DarkGray
}
Write-Host "Remember to match this port in My.Client/wwwroot/appsettings.Development.json (ApiBaseUrl)" -ForegroundColor DarkGray

# Pre-flight: free port if occupied (e.g. previous run left a process)
$portProcesses = Get-NetTCPConnection -LocalPort $Port -ErrorAction SilentlyContinue |
    Select-Object -ExpandProperty OwningProcess -Unique |
    ForEach-Object { Get-Process -Id $_ -ErrorAction SilentlyContinue }
if ($portProcesses) {
    Write-Host "`nPort $Port is already in use by:" -ForegroundColor Yellow
    $portProcesses | Select-Object Id, ProcessName, StartTime | Format-Table -AutoSize
    Write-Host "Killing conflicting process(es)..." -ForegroundColor Yellow
    $portProcesses | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 500
}

try {
    # 1. Ensure SQL Server Docker container is running (portable - no local SQL Server required)
    Write-Host "`n[1/3] Ensuring SQL Server Docker container is running..." -ForegroundColor Yellow
    Write-Host "   (First run / reset: 5-12 min system DB upgrade on the 2022 image is normal. Watch dots or 'docker logs my-workspace-mssql -f'.)" -ForegroundColor DarkGray
    & "$PSScriptRoot\Dev-SetupDockerSql.ps1" -UpdateConnectionString $true
    Write-Host "SQL Server Docker container ready." -ForegroundColor Green

    # 1.5 Trust ASP.NET dev cert (helps when you also run the client, or if falling back to default certs).
    # Fixes ERR_CERT_AUTHORITY_INVALID that users often mis-report as "CORS error" for localhost https.
    if (Test-IsAdministrator) {
        Write-Host "`n[1.5/3] Trusting ASP.NET Core HTTPS development certificate..." -ForegroundColor Yellow
        try {
            dotnet dev-certs https --trust
            Write-Host "  Dev cert trust completed." -ForegroundColor Green
        } catch {
            Write-Warning "dotnet dev-certs https --trust issue: $_"
        }
    }

    # 2. Azurite
    if (-not $NoAzurite) {
        Write-Host "`n[2/3] Checking for Azurite..." -ForegroundColor Yellow

        $azuriteProcess = Get-Process -Name "node" -ErrorAction SilentlyContinue |
                          Where-Object { $_.CommandLine -like '*azurite*' }

        if ($azuriteProcess) {
            Write-Host "Azurite is already running (PID $($azuriteProcess.Id))." -ForegroundColor Green
        } else {
            Write-Host "Azurite not running. Launching in a new PowerShell window..." -ForegroundColor Yellow

            $azuriteDataDir = Join-Path $root "azurite"
            $azuriteCmd = "azurite --location `"$azuriteDataDir`" --silent"

            Start-Process -FilePath "pwsh" `
                          -ArgumentList "-NoExit", "-Command", $azuriteCmd `
                          -WorkingDirectory $root `
                          -WindowStyle Normal

            Start-Sleep -Seconds 3
            Write-Host "Azurite started in separate window." -ForegroundColor Green
        }
    } else {
        Write-Host "`n[2/3] Skipping Azurite (you passed -NoAzurite)." -ForegroundColor DarkGray
    }

    # 3. func host
    Write-Host "`n[3/3] Starting func host..." -ForegroundColor Yellow
    Set-Location $funcAppDir

    $funcArgs = @("--port", $Port)

    if ($UseHttps) {
        $certPath = Join-Path $root "localhost.pfx"
        $certPassword = "devcert"

        Write-Host "Ensuring self-signed certificate for Functions HTTPS..." -ForegroundColor Yellow

        if (-not (Test-Path $certPath)) {
            if (-not (Test-IsAdministrator)) {
                Write-Host "  WARNING: Not running as Administrator." -ForegroundColor Yellow
                Write-Host "  Certificate creation requires elevated privileges on Windows." -ForegroundColor Yellow
                Write-Host ""
                Write-Host "  Please right-click 'Dev-StartFunctionHost.cmd' and choose 'Run as administrator'." -ForegroundColor Cyan
                Write-Host ""
                Write-Host "  Alternatively, create it manually in an *elevated* PowerShell:" -ForegroundColor Yellow
                Write-Host @"
  `$cert = New-SelfSignedCertificate -Subject localhost -DnsName localhost -FriendlyName "Functions Development" -KeyUsage DigitalSignature -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.1")
  Export-PfxCertificate -Cert `$cert -FilePath "$certPath" -Password (ConvertTo-SecureString -String "$certPassword" -Force -AsPlainText)
"@
                Write-Host ""
                Write-Host "  After creating the file, close this window and re-run the .cmd normally." -ForegroundColor Yellow
                Write-Host ""
                Write-Host ">>> HTTPS will NOT be used in this run." -ForegroundColor Red
                Write-Host ">>> All endpoints below will be http:// because no certificate was available." -ForegroundColor Red
                Write-Host ">>> Your client (appsettings.Development.json) expects HTTPS on port 7074." -ForegroundColor Red
                $UseHttps = $false
            } else {
                Write-Host "  Creating localhost.pfx (following Azure Functions recommendation)..." -ForegroundColor DarkGray
                try {
                    $cert = New-SelfSignedCertificate `
                        -Subject "localhost" `
                        -DnsName "localhost" `
                        -FriendlyName "Functions Development" `
                        -KeyUsage DigitalSignature `
                        -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.1")

                    $securePwd = ConvertTo-SecureString -String $certPassword -Force -AsPlainText
                    Export-PfxCertificate -Cert $cert -FilePath $certPath -Password $securePwd | Out-Null

                    # Export public .cer for easy manual trust
                    $cerPath = [System.IO.Path]::ChangeExtension($certPath, ".cer")
                    [System.IO.File]::WriteAllBytes($cerPath, $cert.Export([System.Security.Cryptography.X509Certificates.X509ContentType]::Cert))

                    # Force-trust using certutil (more reliable for browsers)
                    & certutil -addstore -f "Root" "$cerPath" | Out-Null

                    Write-Host "  Certificate created successfully at $certPath (and $cerPath) and forced into Trusted Root via certutil" -ForegroundColor Green
                    Write-Host "  IMPORTANT: Fully close your browser (Task Manager > end ALL msedge.exe / chrome.exe processes) and hard-refresh the app." -ForegroundColor Yellow
                    Write-Host "  To verify: run 'certmgr.msc' > Trusted Root Certification Authorities > Certificates > look for CN=localhost or 'Functions Development'." -ForegroundColor DarkGray
                } catch {
                    Write-Error "Failed to create certificate automatically: $_"
                    $UseHttps = $false
                }
            }
        } else {
            Write-Host "  Using existing certificate at $certPath" -ForegroundColor Green

            # Always ensure .cer exists for manual trust
            $cerPath = [System.IO.Path]::ChangeExtension($certPath, ".cer")
            if (-not (Test-Path $cerPath)) {
                try {
                    $certForCer = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($certPath, $certPassword)
                    [System.IO.File]::WriteAllBytes($cerPath, $certForCer.Export([System.Security.Cryptography.X509Certificates.X509ContentType]::Cert))
                    Write-Host "  Exported $cerPath for manual browser trust." -ForegroundColor Green
                } catch {
                    Write-Warning "Could not export .cer from existing pfx: $_"
                }
            }

            # Force re-trust using certutil (more reliable for browser/WASM fetch)
            & certutil -addstore -f "Root" "$cerPath" | Out-Null
            Write-Host "  Forced re-trust of $cerPath into Trusted Root via certutil." -ForegroundColor Green
        }

        if ($UseHttps) {
            $funcArgs += "--useHttps"
            $funcArgs += "--cert"
            $funcArgs += $certPath
            $funcArgs += "--password"
            $funcArgs += $certPassword
        }
    }

    Write-Host "Running: func start $($funcArgs -join ' ')" -ForegroundColor DarkGray
    Write-Host "Press Ctrl+C in this window to stop the host.`n" -ForegroundColor DarkGray

    func start @funcArgs

    # After starting, the endpoints will be available at https://localhost:$Port
    # (e.g. https://localhost:7074/api/intranet/pages etc.)

} catch {
    Write-Host "`n" -NoNewline
    Write-Host "ERROR: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host $_.ScriptStackTrace -ForegroundColor DarkRed

    if ($Host.Name -eq 'ConsoleHost') {
        Write-Host "`nThe script encountered an error. Press any key to close this window..." -ForegroundColor Yellow
        $null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
    }
    throw
}

# Normal exit (user stopped func with Ctrl+C)
if ($Host.Name -eq 'ConsoleHost') {
    Write-Host "`nFunction host has stopped. Press any key to close this window..." -ForegroundColor Yellow
    $null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
}
