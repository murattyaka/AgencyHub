<#
.SYNOPSIS
  Applies the EF Core migrations of every module to a database (development use).
.DESCRIPTION
  Production deployments use migration bundles instead (see DEPLOYMENT_GUIDE.md).
  Connection: AGENCYHUB_DESIGN_CONNECTION environment variable, otherwise .\SQLEXPRESS / AgencyHub (Windows auth).
.EXAMPLE
  ./scripts/update-database.ps1
#>
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    dotnet tool restore | Out-Null
    # Order is irrelevant (no cross-schema foreign keys) but kept stable for readability.
    foreach ($module in 'Identity', 'Agency', 'Client', 'Project', 'Website') {
        $project = "src/Modules/$module/AgencyHub.Modules.$module"
        Write-Host "==> $module"
        dotnet ef database update --project $project --startup-project $project
        if ($LASTEXITCODE -ne 0) { throw "Migration failed for module $module" }
    }
}
finally {
    Pop-Location
}
