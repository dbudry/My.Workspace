<#
.SYNOPSIS
    Restart only the Blazor WASM client (dotnet watch) without the full debug session.

.DESCRIPTION
    Use when Dev-StartDebugSession is already running (SQL, Azurite, Functions on 7074)
    and you only need to restart the client after a fix — especially when dotnet watch crashed
    or you changed wwwroot JS/CSS and want a clean rebuild.

    - Frees ports 7047 / 5047 and stops orphan My.Client dotnet watch processes
    - Cleans client output by default (prevents stale _framework SRI / integrity errors)
    - Starts dotnet watch on https://localhost:7047

.USAGE
    From the solution root:
        .\Scripts\Dev-StartClient.ps1

    Or double-click Dev-StartClient.cmd.

    Default: kill stale client + dotnet clean + watch.

    Fast restart when watch is healthy:
        .\Scripts\Dev-StartClient.ps1 -NoClean

    Nuclear reset (integrity errors that won't clear):
        .\Scripts\Dev-StartClient.ps1 -FullReset

    Shared-project locks / stale My.Shared builds:
        .\Scripts\Dev-StartClient.ps1 -CleanShared

    New console window:
        .\Scripts\Dev-StartClient.ps1 -NewWindow
#>

[CmdletBinding()]
param(
    [int]$ClientPort = 7047,
    [switch]$NoClean,
    [switch]$Clean,
    [switch]$CleanShared,
    [switch]$FullReset,
    [switch]$NewWindow
)

$ErrorActionPreference = 'Stop'

$scriptRoot = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Path }
. (Join-Path $scriptRoot 'Dev-ClientBoot.ps1')

$root = Get-DevProjectRoot -ScriptsRoot $scriptRoot
$clientDir = Join-Path $root 'My.Client'

Write-Host '=== My.Workspace — Blazor client only ===' -ForegroundColor Cyan
Write-Host "Project root : $root" -ForegroundColor DarkGray
Write-Host "Client dir   : $clientDir" -ForegroundColor DarkGray
Write-Host "Target port  : $ClientPort" -ForegroundColor DarkGray

if (-not (Test-Path $clientDir)) {
    throw "Client project not found at $clientDir"
}

$prepMode = Resolve-DevBlazorClientPrepMode -NoClean:$NoClean -Clean:$Clean -CleanShared:$CleanShared -FullReset:$FullReset

if ($NewWindow) {
    $title = "Blazor Client (port $ClientPort)"
    $bootPath = Join-Path $scriptRoot 'Dev-ClientBoot.ps1'

    Stop-DevBlazorClientListeners -HttpsPort $ClientPort | Out-Null

    # Launch watch directly in a new window (do not re-invoke this script — the nested
    # call used to set the window title first, then Stop-DevBlazorClientListeners killed it).
    $inner = @"
`$ErrorActionPreference = 'Stop'
. '$bootPath'
Stop-DevBlazorClientListeners -HttpsPort $ClientPort | Out-Null
`$Host.UI.RawUI.WindowTitle = '$title'
Start-DevBlazorClientWatch -ClientDir '$clientDir' -Port $ClientPort -PrepMode '$prepMode'
"@
    Start-Process pwsh -ArgumentList '-NoExit', '-Command', $inner -WorkingDirectory $clientDir
    Write-Host 'Opened client in a new window.' -ForegroundColor Green
    return
}

Stop-DevBlazorClientListeners -HttpsPort $ClientPort | Out-Null
Start-DevBlazorClientWatch -ClientDir $clientDir -Port $ClientPort -PrepMode $prepMode