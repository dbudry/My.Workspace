<#
.SYNOPSIS
    Ensures a portable SQL Server Docker container is running for My.Workspace.

.DESCRIPTION
    Handles full lifecycle for Docker-based SQL Server so developers don't need local SQL Server/LocalDB installed:
    - Pulls the image if not present.
    - Creates the container if it does not exist.
    - Starts the container if it exists but is stopped.
    - If the container already exists, shows a 10-second timed prompt allowing the user to press 'R' (or 'r') to RESET (delete and recreate fresh container + DB). Default (no key or timeout) is to keep existing data.
    - Waits for SQL Server to be ready (robust detection for long first-run system DB upgrades).
    - Ensures the development database exists (with READ_COMMITTED_SNAPSHOT).
    - Updates My.AzureFunction/local.settings.json DefaultConnection to use the Docker instance (backs up previous LocalDB string if present).

    Uses SQL Server Express edition (MSSQL_PID=Express) — free, no license needed, sufficient for EF Core, migrations, and dev workloads.
    Production uses Azure SQL Database (compatible).

    Container is capped (default 4g RAM / 2 CPUs) to keep it from eating your machine. First run / after reset of the 2022 image performs a one-time master/model/msdb upgrade that can legitimately take 5-12 minutes. The wait logic is designed for this and gives live status.

.PARAMETER ContainerName
    Docker container name. Default: my-workspace-mssql

.PARAMETER Image
    SQL Server image. Default: mcr.microsoft.com/mssql/server:2022-latest

.PARAMETER HostPort
    Host port mapped to container 1433. Default: 14333 (avoids conflict with local SQL installs on 1433)

.PARAMETER SaPassword
    Password for 'sa' login. Default: DevSql!Passw0rd2026

.PARAMETER DatabaseName
    Development database name. Default: MyWorkspace_Dev

.PARAMETER UpdateConnectionString
    Whether to update the Functions host connection string. Default: $true

.PARAMETER MemoryLimit
    Docker memory limit for the container to keep resource usage low (e.g. "2g", "4g"). Default: 4g

.PARAMETER CpuLimit
    Docker CPU limit (number of CPUs). Default: 2
#>
[CmdletBinding()]
param(
    [string]$ContainerName = "my-workspace-mssql",
    [string]$Image = "mcr.microsoft.com/mssql/server:2022-latest",
    [int]$HostPort = 14333,
    [string]$SaPassword = "DevSql!Passw0rd2026",
    [string]$DatabaseName = "MyWorkspace_Dev",
    [bool]$UpdateConnectionString = $true,
    [string]$MemoryLimit = "4g",
    [string]$CpuLimit = "2"
)

$ErrorActionPreference = 'Stop'

function Wait-ForSqlReady {
    param(
        [string]$Container,
        [string]$Password,
        [int]$TimeoutSeconds = 600
    )
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    Write-Host "Waiting for SQL Server to accept connections..." -ForegroundColor DarkGray
    Write-Host "  (First run or after RESET: SQL 2022 Express does a one-time upgrade of system databases (master etc.). This commonly takes 5-12 minutes. Dots + periodic status will appear; this is normal.)" -ForegroundColor DarkGray
    Write-Host "  Live tail in another window:  docker logs $Container -f" -ForegroundColor DarkGray

    $poll = 0
    $startWait = Get-Date

    while ((Get-Date) -lt $deadline) {
        $poll++
        $ready = $false
        $recentLogs = $null

        # 1. Check recent container logs (larger tail so the "ready" message isn't missed after it scrolls)
        try {
            $recentLogs = docker logs $Container --tail 100 2>$null
            if ($recentLogs -and ($recentLogs -match "SQL Server is now ready for client connections")) {
                $ready = $true
            }
        } catch {}

        if (-not $ready) {
            # 2. Check the persistent errorlog file inside the container (most reliable for the historic ready line)
            try {
                $logCheck = docker exec $Container sh -c "grep -q 'ready for client connections' /var/opt/mssql/log/errorlog 2>/dev/null && echo READY || echo NOTYET" 2>$null
                if ($logCheck -match "READY") { $ready = $true }
            } catch {}
        }

        if (-not $ready) {
            # 3. Ultimate test: can we actually log in and run a query? (sqlcmd path order prefers the one in 2022 images)
            foreach ($sqlcmdPath in @("/opt/mssql-tools18/bin/sqlcmd", "/opt/mssql-tools/bin/sqlcmd")) {
                try {
                    # -C = trust server certificate (required for ODBC Driver 18 / mssql-tools18 on 2022 images)
                    # -l = login timeout (seconds); -b = abort on error; -Q = query; suppress normal output
                    $null = docker exec $Container $sqlcmdPath -S localhost -U sa -P $Password -C -Q "SELECT 1" -l 8 -b -o /dev/null 2>$null
                    if ($LASTEXITCODE -eq 0) {
                        $ready = $true
                        break
                    }
                } catch {}
            }
        }

        if ($ready) {
            Write-Host "`nSQL Server is ready for client connections!" -ForegroundColor Green
            return $true
        }

        # Progress output: dots most of the time, status line every ~10 polls (50s)
        if ($poll % 10 -eq 0) {
            $elapsed = [int]((Get-Date) - $startWait).TotalSeconds
            $status = "  Still starting (elapsed ~$elapsed s)..."
            if ($recentLogs) {
                $lastLine = $recentLogs | Select-Object -Last 6 | Where-Object { $_ -and $_.ToString().Trim() } | Select-Object -Last 1
                if ($lastLine) {
                    $clean = ($lastLine.ToString().Trim() -replace '\s+', ' ')
                    if ($clean.Length -gt 90) { $clean = $clean.Substring(0, 87) + "..." }
                    $status += " Last log: $clean"
                }
            }
            Write-Host $status -ForegroundColor DarkGray
        } else {
            Write-Host "." -NoNewline -ForegroundColor DarkGray
        }

        Start-Sleep -Seconds 5
    }

    Write-Warning "Timed out waiting for SQL Server to be ready after $TimeoutSeconds seconds."
    Write-Host "Container may still be mid-upgrade. Commands to check:" -ForegroundColor Yellow
    Write-Host "    docker logs $Container --tail 100" -ForegroundColor Yellow
    Write-Host "    docker logs $Container -f" -ForegroundColor Yellow
    Write-Host "You can usually just wait a bit longer and re-run Dev-StartDebugSession.ps1 (it will reuse the existing container)." -ForegroundColor Yellow
    return $false
}

function Get-DockerDesktopExecutable {
    $candidates = @(
        (Join-Path $env:ProgramFiles 'Docker\Docker\Docker Desktop.exe')
        (Join-Path ${env:ProgramFiles(x86)} 'Docker\Docker\Docker Desktop.exe')
        (Join-Path $env:LocalAppData 'Programs\Docker\Docker\Docker Desktop.exe')
        (Join-Path $env:LocalAppData 'Docker\Docker Desktop.exe')
    )
    foreach ($candidate in $candidates) {
        if ($candidate -and (Test-Path -LiteralPath $candidate)) {
            return $candidate
        }
    }
    return $null
}

function Test-DockerEngineReachable {
    $hasNativePreference = Test-Path variable:PSNativeCommandUseErrorActionPreference
    $savedPreference = $null
    if ($hasNativePreference) {
        $savedPreference = $PSNativeCommandUseErrorActionPreference
        $PSNativeCommandUseErrorActionPreference = $false
    }
    try {
        $null = docker info 2>&1
        return $LASTEXITCODE -eq 0
    } finally {
        if ($hasNativePreference) {
            $PSNativeCommandUseErrorActionPreference = $savedPreference
        }
    }
}

function Wait-DockerEngine {
    param([int]$TimeoutSeconds = 180)
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        if (Test-DockerEngineReachable) {
            Write-Host ""
            return $true
        }
        Write-Host "." -NoNewline -ForegroundColor DarkGray
        Start-Sleep -Seconds 3
    }
    Write-Host ""
    return $false
}

function Test-DockerDaemonReady {
    $cliCheck = Get-Command docker -ErrorAction SilentlyContinue
    if (-not $cliCheck) {
        return @{
            Ok = $false
            Message = "Docker CLI is not installed or not on PATH. Install Docker Desktop for Windows."
        }
    }

    if (Test-DockerEngineReachable) {
        return @{ Ok = $true; Message = $null }
    }

    Write-Host "Docker Desktop is not running. Starting it..." -ForegroundColor Yellow
    $desktopProcess = Get-Process -Name 'Docker Desktop' -ErrorAction SilentlyContinue
    if (-not $desktopProcess) {
        $exe = Get-DockerDesktopExecutable
        if (-not $exe) {
            return @{
                Ok = $false
                Message = @"
Docker Desktop is not running, and Docker Desktop.exe was not found.
Looked under Program Files, Program Files (x86), and LocalAppData.
Install Docker Desktop, then run Dev-StartDebugSession.cmd again.
"@
            }
        }
        try {
            Start-Process -FilePath $exe | Out-Null
        } catch {
            return @{
                Ok = $false
                Message = "Docker Desktop is not running, and starting '$exe' failed: $($_.Exception.Message)"
            }
        }
    } else {
        Write-Host "Docker Desktop is already starting. Waiting for the engine..." -ForegroundColor DarkGray
    }

    Write-Host "Waiting up to 3 minutes for the Docker engine..." -ForegroundColor DarkGray
    if (Wait-DockerEngine -TimeoutSeconds 180) {
        Write-Host "Docker Desktop is running." -ForegroundColor Green
        return @{ Ok = $true; Message = $null }
    }

    return @{
        Ok = $false
        Message = @"
Docker Desktop did not become ready within 3 minutes. docker info still cannot reach the engine.
Dev-StartDebugSession.cmd stopped.
Start Docker Desktop from the Start menu, wait until it is running, then run Dev-StartDebugSession.cmd again.
To skip Docker SQL: .\Scripts\Dev-StartDebugSession.ps1 -SkipDockerSql
"@
    }
}

Write-Host "=== Setting up SQL Server Docker for My.Workspace ===" -ForegroundColor Cyan

$dockerReady = Test-DockerDaemonReady
if (-not $dockerReady.Ok) {
    Write-Error $dockerReady.Message
    exit 1
}

# 1. Pull image if missing
$images = docker images --format "{{.Repository}}:{{.Tag}}"
if (-not ($images -contains $Image)) {
    Write-Host "Pulling image $Image (first time only)..." -ForegroundColor Yellow
    docker pull $Image
    if ($LASTEXITCODE -ne 0) {
        Write-Error @"
Failed to pull SQL Server image.

If you see 'failed to connect to the docker API' or 'dockerDesktopLinuxEngine', Docker Desktop is not running.
Start Docker Desktop, wait until it is ready, then re-run this script.
"@
        exit 1
    }
} else {
    Write-Host "Image $Image already present locally." -ForegroundColor DarkGray
}

# 2. Handle existing container + timed reset
$existingContainer = docker ps -a --format "{{.Names}}" | Where-Object { $_ -eq $ContainerName }

if ($existingContainer) {
    Write-Host ""
    Write-Host "Container '$ContainerName' already exists." -ForegroundColor Yellow

    # Timed reset prompt - 10 seconds (skip when stdin is redirected, e.g. IDE/automation)
    $resetChosen = $false
    if ([Console]::IsInputRedirected) {
        Write-Host "Non-interactive session — reusing existing container (no reset prompt)." -ForegroundColor DarkGray
    } else {
        $startTime = Get-Date
        $timeout = 10

        Write-Host "Press 'R' (or 'r') within $timeout seconds to RESET (delete container + all data)..." -ForegroundColor Yellow
        Write-Host "Press any other key or wait to continue with existing data." -ForegroundColor DarkGray

        while (((Get-Date) - $startTime).TotalSeconds -lt $timeout) {
            if ([Console]::KeyAvailable) {
                $key = [Console]::ReadKey($true)
                if ($key.KeyChar -eq 'R' -or $key.KeyChar -eq 'r') {
                    $resetChosen = $true
                    Write-Host "`nReset selected by user." -ForegroundColor Red
                    break
                } else {
                    Write-Host "`nContinuing without reset." -ForegroundColor Green
                    break
                }
            }
            $remaining = [math]::Ceiling($timeout - ((Get-Date) - $startTime).TotalSeconds)
            Write-Host "`r  ${remaining}s remaining... " -NoNewline -ForegroundColor DarkGray
            Start-Sleep -Milliseconds 150
        }
        if (-not $resetChosen -and ((Get-Date) - $startTime).TotalSeconds -ge $timeout) {
            Write-Host "`nTimeout - continuing with existing container." -ForegroundColor Green
        }
    }

    if ($resetChosen) {
        Write-Host "Removing existing container for fresh start..." -ForegroundColor Yellow
        docker rm -f $ContainerName | Out-Null
        $existingContainer = $null
    }
}

# 3. Create container if it does not exist now
if (-not $existingContainer) {
    Write-Host "Creating and starting new container '$ContainerName' on port $HostPort..." -ForegroundColor Yellow
    Write-Host "  (After this, readiness wait will begin. First start of the 2022 image performs a lengthy one-time system database upgrade.)" -ForegroundColor DarkGray

    # Defensive remove
    docker rm -f $ContainerName 2>$null | Out-Null

    docker run `
        -e "ACCEPT_EULA=Y" `
        -e "MSSQL_SA_PASSWORD=$SaPassword" `
        -e "MSSQL_PID=Express" `
        -p "${HostPort}:1433" `
        --name $ContainerName `
        --hostname $ContainerName `
        --restart unless-stopped `
        --memory=$MemoryLimit `
        --cpus=$CpuLimit `
        -d `
        $Image

    if ($LASTEXITCODE -ne 0) {
        Write-Error "Failed to create and start SQL container."
        exit 1
    }
} else {
    # Start if stopped
    $running = docker ps --format "{{.Names}}" | Where-Object { $_ -eq $ContainerName }
    if (-not $running) {
        Write-Host "Starting existing container..." -ForegroundColor Yellow
        docker start $ContainerName | Out-Null
    }
}

# 4. Wait for SQL to be ready
# Give the SQL process a couple seconds after start/attach before we start polling hard.
Start-Sleep -Seconds 2

Write-Host "`n[4/4] Waiting for SQL Server readiness (this step can be slow on first run)..." -ForegroundColor Yellow
if (-not (Wait-ForSqlReady -Container $ContainerName -Password $SaPassword)) {
    Write-Error "SQL Server did not become ready in time. Check Docker logs: docker logs $ContainerName"
    exit 1
}

# 5. Ensure database exists and READ_COMMITTED_SNAPSHOT is enabled (required for many EF Core scenarios)
Write-Host "Ensuring database '$DatabaseName' exists and READ_COMMITTED_SNAPSHOT ON..." -ForegroundColor Yellow
$createDb = @"
IF NOT EXISTS (SELECT name FROM sys.databases WHERE name = N'$DatabaseName')
    CREATE DATABASE [$DatabaseName];
GO

IF SERVERPROPERTY('EngineEdition') <> 5
BEGIN
    ALTER DATABASE [$DatabaseName] SET READ_COMMITTED_SNAPSHOT ON;
END
"@
docker exec $ContainerName /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P $SaPassword -C -Q $createDb 2>$null | Out-Null
if ($LASTEXITCODE -ne 0) {
    docker exec $ContainerName /opt/mssql-tools/bin/sqlcmd -S localhost -U sa -P $SaPassword -C -Q $createDb 2>$null | Out-Null
}

Write-Host "Database '$DatabaseName' ready with READ_COMMITTED_SNAPSHOT." -ForegroundColor Green

# 6. Update connection string in local.settings.json (for the Functions host)
if ($UpdateConnectionString) {
    $root = Split-Path -Parent $PSScriptRoot
    $localSettingsPath = Join-Path $root "My.AzureFunction\local.settings.json"

    if (Test-Path $localSettingsPath) {
        try {
            $json = Get-Content $localSettingsPath -Raw | ConvertFrom-Json -AsHashtable -Depth 10
            $dockerConn = "Server=localhost,$HostPort;Database=$DatabaseName;User Id=sa;Password=$SaPassword;TrustServerCertificate=True;MultipleActiveResultSets=true"

            $current = $json.ConnectionStrings.DefaultConnection
            if ($current -and ($current -like "*localdb*" -or $current -like "*MSSQLLocalDB*")) {
                $backupPath = "$localSettingsPath.localdb.bak"
                if (-not (Test-Path $backupPath)) {
                    Copy-Item $localSettingsPath $backupPath -Force
                    Write-Host "Backed up original LocalDB connection to $backupPath" -ForegroundColor DarkGray
                }
            }

            if (-not $json.ConnectionStrings) {
                $json.ConnectionStrings = @{}
            }
            $json.ConnectionStrings.DefaultConnection = $dockerConn

            $json | ConvertTo-Json -Depth 10 | Set-Content $localSettingsPath -Encoding UTF8
            Write-Host "Updated My.AzureFunction/local.settings.json to use Docker SQL connection." -ForegroundColor Green
        } catch {
            Write-Warning "Failed to update local.settings.json automatically. Manually set ConnectionStrings:DefaultConnection to:`nServer=localhost,$HostPort;Database=$DatabaseName;User Id=sa;Password=$SaPassword;TrustServerCertificate=True;MultipleActiveResultSets=true"
        }
    }
}

Write-Host "`nSQL Docker setup complete. Container will restart automatically on Docker restart." -ForegroundColor Cyan
Write-Host "Connection string in use: Server=localhost,$HostPort;Database=$DatabaseName;User=sa;..." -ForegroundColor DarkGray