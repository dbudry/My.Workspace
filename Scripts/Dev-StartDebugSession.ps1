<#
.SYNOPSIS
    One script to rule them all: Starts everything needed for local debugging
    of the Azure Functions host + Blazor client, then attaches the debugger.

.DESCRIPTION
    This single script:
    - Ensures a SQL Server Docker container is running (pulls image / creates container / starts as needed)
    - Starts Azurite (in a separate window) if not already running
    - Starts the Functions host (func start) in a separate window on port 7074 (clean, no debugger attached during startup)
    - Starts the Blazor client (dotnet watch) in a separate window on port 7047
    - Waits for the Functions worker process to appear
    - Attaches the Visual Studio debugger to the worker so your breakpoints work

    The SQL container logic makes the project portable — other developers only need Docker,
    not a local SQL Server / LocalDB installation.

    Google OAuth is configured for these ports (7074 for API callbacks, 7047 for client).
    Pass -UseHttps to start the Functions host with HTTPS (required if client expects https://localhost:7074).

    Run this from the solution root or double-click the .cmd wrapper.
    It is designed to work even when launched via Visual Studio "Open Command Line".

    Defaults to HTTPS on 7074 using explicit localhost.pfx (to match client).
    Automatically kills any previous Blazor client on ports 7047/5047, cleans client
    output before watch (prevents WASM integrity / 99% boot errors), and starts
    exactly one dotnet watch instance.

    IMPORTANT: Creating the development certificate usually requires Administrator rights.
    → Right-click Dev-StartDebugSession.cmd and select "Run as administrator".

    To force plain HTTP:
        .\Scripts\Dev-StartDebugSession.ps1 -UseHttps:$false
#>

param(
    [int]$FunctionsPort = 7074,
    [int]$ClientPort = 7047,
    [switch]$UseHttps,
    [switch]$ClientFullReset,
    [switch]$SkipDockerSql
)

# Default to HTTPS to match the client's https://localhost:7074/api/ expectation.
if (-not $PSBoundParameters.ContainsKey('UseHttps')) {
    $UseHttps = [switch]$true
}

function Test-IsAdministrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

$ErrorActionPreference = 'Stop'

# Kill any lingering build servers and dotnet processes from previous sessions.
# This prevents "file is being used by another process" (VBCSCompiler) errors on My.Shared.dll etc.
# We add the entire project root to Defender exclusions (permanent fix) and temporarily
# disable realtime monitoring during the critical cleanup + launch window.
Write-Host "Shutting down previous build servers and cleaning up dotnet processes..." -ForegroundColor Yellow

$scriptRootEarly = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Path }
$projectRoot = Split-Path -Parent $scriptRootEarly
try {
    $prefs = Get-MpPreference -ErrorAction SilentlyContinue
    if ($prefs -and $prefs.ExclusionPath -notcontains $projectRoot) {
        Add-MpPreference -ExclusionPath $projectRoot -ErrorAction SilentlyContinue
        Write-Host "  Added $projectRoot to Windows Defender exclusions (this prevents future locks during builds)." -ForegroundColor Green
    }
    Set-MpPreference -DisableRealtimeMonitoring $true -ErrorAction SilentlyContinue
    Write-Host "  Temporarily disabled Windows Defender realtime monitoring during cleanup and launch." -ForegroundColor DarkGray
} catch {}

dotnet build-server shutdown 2>$null | Out-Null
taskkill /F /IM VBCSCompiler.exe /T 2>$null | Out-Null
taskkill /F /IM MSBuild.exe /T 2>$null | Out-Null

# Kill any old script-launched windows by their titles (the sub-processes started with -NoExit)
Get-Process -Name pwsh,powershell -ErrorAction SilentlyContinue |
    Where-Object { $_.MainWindowTitle -like '*Functions Host*' -or $_.MainWindowTitle -like '*Blazor Client*' } |
    Stop-Process -Force -ErrorAction SilentlyContinue

Get-Process -Name VBCSCompiler, dotnet, MSBuild, func -ErrorAction SilentlyContinue | 
    Where-Object { $_.MainWindowTitle -notlike '*Visual Studio*' -and $_.MainWindowTitle -notlike '*Code*' } | 
    Stop-Process -Force -ErrorAction SilentlyContinue

Start-Sleep -Milliseconds 1200

# Force release locks by removing the locked obj folders (safe - next build regenerates)
# Retry loop with kills inside.
$pathsToNuke = @(
    (Join-Path $projectRoot "My.Shared\obj\Debug\net10.0"),
    (Join-Path $projectRoot "My.AzureFunction\obj\Debug\net10.0"),
    (Join-Path $projectRoot "My.AzureFunction\bin\output")
)

foreach ($p in $pathsToNuke) {
    if (Test-Path $p) {
        Write-Host "Force-removing locked folder: $p ..." -ForegroundColor Yellow
        for ($i=0; $i -lt 8; $i++) {
            try {
                Remove-Item -Recurse -Force $p -ErrorAction Stop
                Write-Host "  Removed $p on attempt $($i+1)" -ForegroundColor Green
                break
            } catch {
                Write-Host "  Retry $($i+1)/8 removing $p (still locked)..." -ForegroundColor DarkGray
                taskkill /F /IM VBCSCompiler.exe /T 2>$null | Out-Null
                taskkill /F /IM MSBuild.exe /T 2>$null | Out-Null
                Start-Sleep -Milliseconds 500
            }
        }
    }
}

# Pre-clean (in case removes didn't catch everything)
dotnet clean "$(Join-Path $projectRoot 'My.Shared\My.Shared.csproj')" -c Debug --nologo -v q 2>$null | Out-Null
dotnet clean "$(Join-Path $projectRoot 'My.DAL\My.DAL.csproj')" -c Debug --nologo -v q 2>$null | Out-Null
dotnet clean "$(Join-Path $projectRoot 'My.AzureFunction\My.AzureFunction.csproj')" -c Debug --nologo -v q 2>$null | Out-Null

Start-Sleep -Milliseconds 800

# Explicitly build the shared class libraries *before* launching the Functions host.
# This ensures the reference assemblies (obj\Debug\net10.0\ref\*.dll) are generated.
# The aggressive obj nuke + clean above can leave the Functions build unable to find
# metadata for project references, causing CS0006 errors inside the host window.
Write-Host "Building shared projects to populate reference assemblies..." -ForegroundColor Yellow
dotnet build "$(Join-Path $projectRoot 'My.Shared\My.Shared.csproj')" -c Debug --nologo -v q
dotnet build "$(Join-Path $projectRoot 'My.DAL\My.DAL.csproj')" -c Debug --nologo -v q

Start-Sleep -Milliseconds 400

# Re-enable Defender realtime (best effort) -- note: with the exclusion above, this is less critical
try {
    Set-MpPreference -DisableRealtimeMonitoring $false -ErrorAction SilentlyContinue
} catch {}

# Robust self-location (works from VS context menus, different cwd, etc.)
$scriptRoot = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Path }
Set-Location $scriptRoot

$root = Split-Path -Parent $scriptRoot
$funcDir = Join-Path $root "My.AzureFunction"
$clientDir = Join-Path $root "My.Client"

Write-Host "=== My.Workspace Full Debug Session ===" -ForegroundColor Cyan
Write-Host "Project root : $root" -ForegroundColor DarkGray
Write-Host "Functions dir: $funcDir (port $FunctionsPort)" -ForegroundColor DarkGray
if ($UseHttps) {
    Write-Host "Functions HTTPS: Enabled (--useHttps)" -ForegroundColor DarkGray
} else {
    Write-Host "Functions HTTPS: Disabled (HTTP)" -ForegroundColor DarkGray
}
Write-Host "Client dir   : $clientDir (port $ClientPort)" -ForegroundColor DarkGray

# 1. SQL Server via Docker (portable - works even if developer has no local SQL/LocalDB)
if ($SkipDockerSql) {
    Write-Host "`n[1/5] Skipping SQL Docker setup (-SkipDockerSql). Using DefaultConnection from local.settings.json." -ForegroundColor Yellow
} else {
    Write-Host "`n[1/5] Ensuring SQL Server Docker container is running..." -ForegroundColor Yellow
    Write-Host "   (Requires Docker Desktop to be running. On first run or after reset this can take 5-12 minutes while SQL 2022 Express upgrades its internal system databases. Be patient or watch 'docker logs my-workspace-mssql -f' in another window.)" -ForegroundColor DarkGray
    & "$PSScriptRoot\Dev-SetupDockerSql.ps1" -UpdateConnectionString $true
    Write-Host "SQL Server Docker container ready (see Dev-SetupDockerSql.ps1 output for connection details)." -ForegroundColor Green
}

# 1.5 Dev cert trust (critical for client https://localhost:7047 browser fetches + perceived CORS issues).
# The Blazor `dotnet watch` server uses the ASP.NET Core dev cert (separate from the Functions localhost.pfx).
# Without this, you get ERR_CERT_AUTHORITY_INVALID on 7047 (bootstrap _framework/*.js) and cross-origin
# fetches to 7074 often surface as "CORS error" in the console even when the root cause is TLS.
if (Test-IsAdministrator) {
    Write-Host "`n[1.5/5] Trusting ASP.NET Core development certificate for the Blazor client (https://localhost:7047)..." -ForegroundColor Yellow
    try {
        # --trust is idempotent; it installs/repairs the cert in CurrentUser\Root so Edge/Chrome trust the Kestrel server.
        dotnet dev-certs https --trust
        Write-Host "  ASP.NET dev cert trust step completed. (Restart browser fully if you still see warnings.)" -ForegroundColor Green
    } catch {
        Write-Warning "dotnet dev-certs https --trust had an issue: $_"
        Write-Host "  You can run it manually in an admin PowerShell: dotnet dev-certs https --trust" -ForegroundColor Yellow
    }
} else {
    Write-Host "  (Not admin: skipping automatic 'dotnet dev-certs https --trust'. Client on 7047 may still show cert errors until you do this elevated.)" -ForegroundColor DarkGray
}

# 2. Azurite (in its own window)
Write-Host "`n[2/5] Checking Azurite..." -ForegroundColor Yellow
$azuriteRunning = Get-Process -Name node -ErrorAction SilentlyContinue | Where-Object { $_.CommandLine -like '*azurite*' }
if ($azuriteRunning) {
    Write-Host "Azurite already running." -ForegroundColor Green
} else {
    Write-Host "Starting Azurite in a new window..." -ForegroundColor Yellow
    $azuriteDir = Join-Path $root "azurite"
    $azuriteCmd = "azurite --location `"$azuriteDir`" --silent"
    Start-Process pwsh -ArgumentList "-NoExit", "-Command", $azuriteCmd -WorkingDirectory $root
    Start-Sleep -Seconds 3
    Write-Host "Azurite started." -ForegroundColor Green
}

# Pre-flight for Functions port (7074) - rare but can happen
$hostPortProcesses = Get-NetTCPConnection -LocalPort $FunctionsPort -ErrorAction SilentlyContinue |
    Select-Object -ExpandProperty OwningProcess -Unique |
    ForEach-Object { Get-Process -Id $_ -ErrorAction SilentlyContinue }
if ($hostPortProcesses) {
    Write-Host "`nPort $FunctionsPort is already in use by:" -ForegroundColor Yellow
    $hostPortProcesses | Select-Object Id, ProcessName, StartTime | Format-Table -AutoSize
    Write-Host "Killing conflicting process(es)..." -ForegroundColor Yellow
    $hostPortProcesses | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 500
}

# 3. Start the Functions host in a separate window (clean startup, no debugger during init)
Write-Host "`n[3/5] Starting Functions host in a separate window (clean, no debugger attached during startup)..." -ForegroundColor Yellow
$hostTitle = "Functions Host (port $FunctionsPort)"

$certPath = Join-Path $root "localhost.pfx"
$certPassword = "devcert"

if ($UseHttps) {
    Write-Host "Ensuring self-signed certificate for Functions HTTPS..." -ForegroundColor Yellow

    if (-not (Test-Path $certPath)) {
        if (-not (Test-IsAdministrator)) {
            Write-Host "  WARNING: Not running as Administrator." -ForegroundColor Yellow
            Write-Host "  Certificate creation requires elevated privileges on Windows." -ForegroundColor Yellow
            Write-Host ""
            Write-Host "  Please right-click 'Dev-StartDebugSession.cmd' and choose 'Run as administrator'." -ForegroundColor Cyan
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
            Write-Host ">>> The Functions host window will start on plain HTTP." -ForegroundColor Red
            Write-Host ">>> Your client expects HTTPS on 7074." -ForegroundColor Red
            $UseHttps = $false
            Write-Host ""
            Write-Host "IMPORTANT: Manually double-click the generated localhost.cer (preferred) or .pfx and during the wizard:" -ForegroundColor Yellow
            Write-Host "  - Choose Current User" -ForegroundColor Yellow
            Write-Host "  - Select 'Place all certificates in the following store'" -ForegroundColor Yellow
            Write-Host "  - Browse and pick 'Trusted Root Certification Authorities'" -ForegroundColor Yellow
            Write-Host "Then fully kill your browser processes and restart the script normally." -ForegroundColor Yellow
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

                # Export public .cer for easy manual trust in browser/OS
                $cerPath = [System.IO.Path]::ChangeExtension($certPath, ".cer")
                [System.IO.File]::WriteAllBytes($cerPath, $cert.Export([System.Security.Cryptography.X509Certificates.X509ContentType]::Cert))

                # Force-trust using certutil (more reliable for browsers than PowerShell store add)
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

        # Force re-trust using certutil (more reliable for browser/WASM fetch than PowerShell store)
        & certutil -addstore -f "Root" "$cerPath" | Out-Null
        Write-Host "  Forced re-trust of $cerPath into Trusted Root via certutil." -ForegroundColor Green
    }
}

$hostCmd = @"
`$Host.UI.RawUI.WindowTitle = '$hostTitle'
cd '$funcDir'

# Do NOT clean the shared projects here (My.Shared / My.DAL).
# The main script already did a clean + explicit build of them (to populate ref assemblies
# in obj\...\ref\). Cleaning them again here would delete the reference metadata that
# the Azure Functions build needs, causing CS0006 "Metadata file ... \ref\*.dll could not be found".
# Only clean the function app project itself.
Write-Host 'Cleaning function app project (shared projects were pre-built by main script to ensure ref metadata)...' -ForegroundColor Yellow
dotnet clean . -c Debug --nologo -v q   # clean the function app itself

Write-Host 'Ensuring self-signed certificate for Functions HTTPS...' -ForegroundColor Yellow
Write-Host '  Using existing certificate: $certPath' -ForegroundColor Green
"@

if ($UseHttps) {
    $hostCmd += @"

func start --port $FunctionsPort --useHttps --cert '$certPath' --password $certPassword
"@
} else {
    $hostCmd += @"

func start --port $FunctionsPort
"@
}

Start-Process pwsh -ArgumentList "-NoExit", "-Command", $hostCmd -WorkingDirectory $root
Write-Host "Functions host window opened." -ForegroundColor Green

# Give the Functions host build a head start so it can finish building My.Shared / My.DAL
# before the client dotnet watch also tries to build the same shared projects.
# This reduces VBCSCompiler / file lock contention between the two builds.
Write-Host "Waiting a bit for the Functions host build to settle before starting the client..." -ForegroundColor Yellow
Start-Sleep -Seconds 12

# 4. Start the Blazor client via the shared boot helper (same path as Dev-StartClient.ps1).
Write-Host "`n[4/5] Starting Blazor client in a separate window on port $ClientPort..." -ForegroundColor Yellow
$clientBootScript = Join-Path $PSScriptRoot 'Dev-StartClient.ps1'
$clientArgs = @{
    ClientPort = $ClientPort
    NewWindow  = $true
}
if ($ClientFullReset) {
    Write-Host "ClientFullReset: wiping client bin/obj before watch (use after stubborn SRI / 99% errors)." -ForegroundColor Yellow
    $clientArgs['FullReset'] = $true
} else {
    # Clean ONLY the client here, not the shared projects. The main script already cleaned
    # + explicitly rebuilt My.Shared / My.DAL above, and the Functions host (started ~12s ago)
    # is already building/running against those same DLLs. Re-cleaning them in the client window
    # (the old -CleanShared default) yanked the shared bin/obj out from under the running host,
    # causing VBCSCompiler locks + CS0006 and forcing a full client rebuild every launch.
    $clientArgs['Clean'] = $true
}

& $clientBootScript @clientArgs
Write-Host "Blazor client window opened." -ForegroundColor Green

# 5. Wait for the Functions worker process and attach debugger
Write-Host "`n[5/5] Waiting for Functions worker process to appear and attaching debugger..." -ForegroundColor Yellow

$deadline = (Get-Date).AddSeconds(90)
$worker = $null

while ((Get-Date) -lt $deadline -and -not $worker) {
    $worker = Get-Process -Name dotnet -ErrorAction SilentlyContinue |
        Where-Object { 
            $_.CommandLine -like '*My.AzureFunction*' -or 
            $_.CommandLine -like '*bin\output*' 
        } |
        Sort-Object StartTime -Descending |
        Select-Object -First 1

    if (-not $worker) {
        Write-Host "." -NoNewline -ForegroundColor DarkGray
        Start-Sleep -Seconds 2
    }
}
Write-Host ""

if (-not $worker) {
    Write-Warning "Timed out waiting for the worker process."
    Write-Host "The host and client windows are still running."
    Write-Host "You can attach manually in VS: Debug > Attach to Process (look for the dotnet worker PID)."
    Read-Host "Press Enter to close this window"
    exit
}

Write-Host "Worker found (PID $($worker.Id))." -ForegroundColor Yellow
Write-Host "Command line: $($worker.CommandLine)" -ForegroundColor DarkGray

# Give VS a moment to see the newly started worker (helps with timing)
Start-Sleep -Seconds 3

Write-Host "Attempting to auto-attach debugger..." -ForegroundColor Yellow

# Connect to a running Visual Studio instance.
# This uses COM (GetActiveObject) and is sensitive to elevation level.
# If you ran this script as Administrator (recommended for certs/cleanup), VS must also be elevated.
$dte = $null
try { $dte = [Runtime.InteropServices.Marshal]::GetActiveObject('VisualStudio.DTE.17.0') } catch {}
if (-not $dte) {
    try { $dte = [Runtime.InteropServices.Marshal]::GetActiveObject('VisualStudio.DTE.18.0') } catch {}
}

$attached = $false
if ($dte) {
    try {
        $vsProc = $dte.Debugger.LocalProcesses | Where-Object { $_.ProcessID -eq $worker.Id }
        if ($vsProc) {
            $vsProc.Attach()
            Write-Host "SUCCESS: Debugger attached to PID $($worker.Id) via Visual Studio COM!" -ForegroundColor Green
            Write-Host "Switch to Visual Studio — your breakpoints should now be hit." -ForegroundColor Green
            $attached = $true
        } else {
            Write-Warning "The worker (PID $($worker.Id)) is not yet listed in VS's LocalProcesses."
        }
    } catch {
        Write-Warning "Error while trying to attach via DTE: $_"
    }
}

if (-not $attached) {
    $isAdmin = Test-IsAdministrator
    Write-Warning "Could not auto-attach to the debugger."

    if ($isAdmin) {
        Write-Host ""
        Write-Host "You are running this script as Administrator." -ForegroundColor Yellow
        Write-Host "Visual Studio must also be running as Administrator for COM auto-attach to work." -ForegroundColor Yellow
        Write-Host ""
        Write-Host "Quick options:" -ForegroundColor Cyan
        Write-Host "  1. Close this window, launch Visual Studio as Administrator, then re-run the debug script." -ForegroundColor White
        Write-Host "  2. Attach manually right now (easiest):" -ForegroundColor White
    } else {
        Write-Host ""
        Write-Host "Quick options:" -ForegroundColor Cyan
    }

    Write-Host "     In Visual Studio:  Debug > Attach to Process..." -ForegroundColor White
    Write-Host "     Filter by PID or look for:" -ForegroundColor White
    Write-Host "       - Process: dotnet.exe" -ForegroundColor White
    Write-Host "       - PID:     $($worker.Id)" -ForegroundColor White
    Write-Host "       - Title / Command line should mention: My.AzureFunction or bin\output" -ForegroundColor White
    Write-Host ""
    Write-Host "     (There can be several dotnet.exe processes — pick the one that matches the command line above.)" -ForegroundColor DarkGray
}

Write-Host "`nHost and client are running in their own windows."
Write-Host "This setup window can be closed when ready."
Read-Host "Press Enter to exit"

Write-Host "`nHost and client are running in their own windows."
Write-Host "This setup window can be closed when ready."
Read-Host "Press Enter to exit"
