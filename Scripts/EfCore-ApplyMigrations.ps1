<#
.SYNOPSIS
    Applies pending EF Core schema migrations to the local development database.

.DESCRIPTION
    Runs `dotnet ef database update` for My.DAL using the connection string from
    My.AzureFunction/local.settings.json (via Design

    Use this after pulling new migrations or creating one with EfCore-AddMigration.ps1.
    This updates **schema only** — it does not import legacy 
    For that, use 

.EXAMPLE
    .\Scripts\EfCore-ApplyMigrations.ps1
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$location = Get-Location

Write-Host 'Ensuring dotnet-ef tool is installed...' -ForegroundColor Yellow
dotnet tool install --global dotnet-ef --ignore-failed-sources 2>$null | Out-Null

$root = Split-Path -Parent $PSScriptRoot
$dalProject = Join-Path $root 'My.DAL\My.DAL.csproj'
$functionsProject = Join-Path $root 'My.AzureFunction\My.AzureFunction.csproj'

Write-Host 'Applying EF Core migrations (My.DAL) using local.settings.json connection...' -ForegroundColor Cyan

Set-Location (Split-Path $functionsProject)
dotnet build --nologo -v q $functionsProject
dotnet ef database update --no-build --project $dalProject --startup-project $functionsProject

Write-Host 'EF Core migrations applied.' -ForegroundColor Green
Set-Location $location