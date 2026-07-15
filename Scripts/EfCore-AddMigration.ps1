<#
.SYNOPSIS
    Creates a new EF Core schema migration in My.DAL/Data/Migrations.

.DESCRIPTION
    Wrapper around `dotnet ef migrations add` for My.Workspace.

    Use this when you change entities or DbContext in My.DAL and need a new
    **schema** migration (tables, columns, indexes). This is NOT the legacy
    

.PARAMETER Name
    Migration name, e.g. AddIntranetFavorites

.EXAMPLE
    .\Scripts\EfCore-AddMigration.ps1 AddIntranetFavorites
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [string]$Name
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$dalProject = Join-Path $root 'My.DAL\My.DAL.csproj'
$functionsProject = Join-Path $root 'My.AzureFunction\My.AzureFunction.csproj'

Write-Host "Creating EF Core migration '$Name' in My.DAL/Data/Migrations ..." -ForegroundColor Cyan
dotnet ef migrations add $Name `
    --project $dalProject `
    --startup-project $functionsProject `
    --output-dir Data/Migrations

Write-Host "Done. Review the generated files, then run .\Scripts\EfCore-ApplyMigrations.ps1 to apply locally." -ForegroundColor Green