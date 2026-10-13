#Requires -Version 7
<#
.SYNOPSIS
    Runs Keystone locally the way it runs in Azure.

.DESCRIPTION
    Builds both SPAs (npm run build), lays them out like the BFF container image built by
    `aspire deploy` (keystone at wwwroot/, keystone-admin at wwwroot/admin/), and starts the AppHost
    with the BFF in Production mode serving those files. Use it to check the production-only code
    paths (static files, client-side route fallbacks) before deploying.

    Everything else stays local: Zitadel, SQL Server and Redis containers, the same https://localhost:7003.
#>
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$webRoot = Join-Path $root 'artifacts/wwwroot'

foreach ($app in 'keystone', 'keystone-admin') {
    Push-Location (Join-Path $root $app)
    try {
        npm run build
        if ($LASTEXITCODE -ne 0) { throw "$app build failed" }
    }
    finally { Pop-Location }
}

New-Item -ItemType Directory -Force (Join-Path $webRoot 'admin') | Out-Null
Copy-Item (Join-Path $root 'keystone/dist/*') $webRoot -Recurse -Force
Copy-Item (Join-Path $root 'keystone-admin/dist/*') (Join-Path $webRoot 'admin') -Recurse -Force
Write-Host "SPAs laid out in $webRoot"

dotnet run --project (Join-Path $root 'Keystone.AppHost') -- --Keystone:PreviewWebRoot=$webRoot
