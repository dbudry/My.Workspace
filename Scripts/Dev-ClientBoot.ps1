<#
.SYNOPSIS
    Shared helpers for starting the Blazor WASM client without stale _framework assets.

.DESCRIPTION
    Dot-source from Dev-StartClient.ps1 and Dev-StartDebugSession.ps1.
    Centralizes port cleanup, dotnet clean/full-reset, and dotnet watch launch so
    both entry points behave the same way.
#>

function Get-DevProjectRoot {
    param([string]$ScriptsRoot)
    Split-Path -Parent $ScriptsRoot
}

function Stop-DevBlazorClientListeners {
    param(
        [int]$HttpsPort = 7047,
        [int]$HttpPort = 5047
    )

    $processes = @()
    foreach ($port in @($HttpsPort, $HttpPort)) {
        $processes += Get-NetTCPConnection -LocalPort $port -ErrorAction SilentlyContinue |
            Select-Object -ExpandProperty OwningProcess -Unique |
            ForEach-Object { Get-Process -Id $_ -ErrorAction SilentlyContinue }
    }

    # Stale script-launched client windows from a previous debug session.
    # Never kill the current shell — the -NewWindow path sets this title before cleanup.
    $currentPid = $PID
    $processes += Get-Process -Name pwsh,powershell -ErrorAction SilentlyContinue |
        Where-Object { $_.Id -ne $currentPid -and $_.MainWindowTitle -like '*Blazor Client*' }

    # Orphan dotnet watch/run for My.Client (common when port was already freed).
    $processes += Get-Process -Name dotnet -ErrorAction SilentlyContinue |
        Where-Object {
            $cmd = $_.CommandLine
            $cmd -and ($cmd -like '*My.Client*') -and ($cmd -like '*watch*' -or $cmd -like '*run*')
        }

    $processes = $processes | Where-Object { $_ } | Sort-Object Id -Unique
    if (-not $processes) { return $false }

    Write-Host "Stopping previous Blazor client process(es) on port(s) $HttpsPort / $HttpPort..." -ForegroundColor Yellow
    $processes | Select-Object Id, ProcessName, StartTime | Format-Table -AutoSize
    $processes | Stop-Process -Force -ErrorAction SilentlyContinue
    Start-Sleep -Milliseconds 800
    return $true
}

function Invoke-DevBlazorClientPrep {
    param(
        [string]$ClientDir,
        [string]$SharedProj,
        [string]$DalProj,
        [ValidateSet('None', 'Clean', 'CleanShared', 'FullReset')]
        [string]$PrepMode = 'Clean'
    )

    Set-Location $ClientDir

    switch ($PrepMode) {
        'FullReset' {
            Write-Host "Full reset: removing client bin/obj (fixes stubborn SRI / 404 on _framework)..." -ForegroundColor Yellow
            Remove-Item -Recurse -Force (Join-Path $ClientDir 'bin'), (Join-Path $ClientDir 'obj') -ErrorAction SilentlyContinue
        }
        'CleanShared' {
            Write-Host "Cleaning shared projects + client (prevents stale _framework hashes / file locks)..." -ForegroundColor DarkGray
            dotnet clean $SharedProj -c Debug --nologo -v q
            dotnet clean $DalProj -c Debug --nologo -v q
            dotnet clean --nologo -v q
        }
        'Clean' {
            Write-Host "Cleaning client output..." -ForegroundColor DarkGray
            dotnet clean --nologo -v q
        }
        default { }
    }
}

function Start-DevBlazorClientWatch {
    param(
        [string]$ClientDir,
        [int]$Port = 7047,
        [ValidateSet('None', 'Clean', 'CleanShared', 'FullReset')]
        [string]$PrepMode = 'Clean'
    )

    Invoke-DevBlazorClientPrep -ClientDir $ClientDir -SharedProj (Join-Path (Split-Path -Parent $ClientDir) 'My.Shared\My.Shared.csproj') `
        -DalProj (Join-Path (Split-Path -Parent $ClientDir) 'My.DAL\My.DAL.csproj') -PrepMode $PrepMode

    # Watch can start the dev server before the Blazor output step writes
    # My.Client.staticwebassets.endpoints.json (especially right after clean / full reset
    # or when the file watcher restarts mid-build). A full build first avoids that race.
    Write-Host "Building client (staticwebassets manifest)..." -ForegroundColor DarkGray
    dotnet build -c Debug --nologo
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet build failed — fix build errors before starting dotnet watch."
    }

    Write-Host "Starting dotnet watch on https://localhost:$Port ..." -ForegroundColor Green
    Write-Host "Only ONE client should listen on $Port. If the browser sticks at 99%, hard-refresh (Ctrl+Shift+R)." -ForegroundColor DarkGray
    Write-Host "(Ctrl+C to stop. Functions host on 7074 can keep running.)" -ForegroundColor DarkGray
    dotnet watch --urls "https://localhost:$Port"
}

function Resolve-DevBlazorClientPrepMode {
    param(
        [switch]$NoClean,
        [switch]$Clean,
        [switch]$CleanShared,
        [switch]$FullReset
    )

    if ($FullReset.IsPresent) { return 'FullReset' }
    if ($CleanShared.IsPresent) { return 'CleanShared' }
    if ($Clean.IsPresent) { return 'Clean' }
    if ($NoClean.IsPresent) { return 'None' }
    return 'Clean'
}