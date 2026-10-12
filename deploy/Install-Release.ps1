<#
  Installs a built release on the production machine: stops the CallingBell service, replaces the API (C:\CallingBell\api) and the web
  app (C:\CallingBell\spa), optionally runs the database scripts, starts the service and checks /health. If the new release doesn't
  come up healthy, the previous one is put back and the script fails.

  -Source is a folder holding api\ and web\ (and database\scripts for -SqlScripts): the pipeline artifact, or the repo's publish folder.
  App_Data (GeoIP databases, GeoNames, search index) is never touched. Settings and secrets live in the service's environment.

    .\deploy\Install-Release.ps1 -Source D:\projects\callingbellfinal\publish
#>
param(
    [Parameter(Mandatory)][string]$Source,
    [string]$Target = 'C:\CallingBell',
    [string]$ServiceName = 'CallingBell',
    [string]$HealthUrl = 'http://127.0.0.1:8080/health',
    [string]$Database = 'CallingBell_Prod',
    [switch]$SqlScripts
)
$ErrorActionPreference = 'Stop'

# robocopy exit codes below 8 mean success (1 = files copied); reset so callers checking $LASTEXITCODE don't see a failure.
function Copy-Tree([string]$From, [string]$To, [switch]$Mirror) {
    $options = @('/NFL', '/NDL', '/NJH', '/NJS', '/NP', '/R:3', '/W:2', '/XD', 'App_Data')
    $options = @(if ($Mirror) { '/MIR' } else { '/E' }) + $options
    robocopy $From $To @options | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "robocopy $From -> $To failed (exit $LASTEXITCODE)" }
    $global:LASTEXITCODE = 0
}

# Asks as the Cloudflare Tunnel does (X-Forwarded-Proto: https from loopback): a plain http request is redirected to https://127.0.0.1,
# which fails the certificate check because the certificate is for callinbell.com.
$healthHeaders = @{ 'X-Forwarded-Proto' = 'https' }
function Get-Health { Invoke-WebRequest $HealthUrl -Headers $healthHeaders -UseBasicParsing -TimeoutSec 5 -MaximumRedirection 0 }

function Test-Healthy {
    foreach ($attempt in 1..12) {
        Start-Sleep -Seconds 5
        try { if ((Get-Health).StatusCode -eq 200) { return $true } } catch { }
    }
    return $false
}

foreach ($part in 'api\CallingBell.Api.exe', 'web\index.html') {
    if (-not (Test-Path (Join-Path $Source $part))) { throw "$Source is not a release: $part is missing" }
}

$previous = Join-Path $Target 'previous'
Write-Host "Keeping the current release in $previous"
Copy-Tree "$Target\api" "$previous\api" -Mirror
Copy-Tree "$Target\spa" "$previous\spa" -Mirror

Write-Host "Stopping $ServiceName"
Stop-Service $ServiceName
(Get-Service $ServiceName).WaitForStatus('Stopped', [TimeSpan]::FromSeconds(60))

try {
    if ($SqlScripts) {
        Write-Host "Running database scripts against $Database"
        Push-Location (Join-Path $Source 'database\scripts')
        try {
            sqlcmd -S .\SQLEXPRESS -E -C -I -f 65001 -b -d $Database -i RunAll.sql
            if ($LASTEXITCODE) { throw "database scripts failed (exit $LASTEXITCODE)" }
        } finally { Pop-Location }
    }

    Write-Host 'Copying the new release'
    Copy-Tree (Join-Path $Source 'api') "$Target\api"
    Copy-Tree (Join-Path $Source 'web') "$Target\spa" -Mirror

    Start-Service $ServiceName
    if (-not (Test-Healthy)) { throw "$HealthUrl did not report healthy within a minute" }
    Write-Host "Release installed: $((Get-Health).Content)"
}
catch {
    Write-Warning "Deploy failed: $($_.Exception.Message). Restoring the previous release."
    Stop-Service $ServiceName -ErrorAction SilentlyContinue
    Copy-Tree "$previous\api" "$Target\api" -Mirror
    Copy-Tree "$previous\spa" "$Target\spa" -Mirror
    Start-Service $ServiceName
    if (Test-Healthy) { Write-Warning 'Previous release restored and healthy.' } else { Write-Warning 'Previous release restored but NOT healthy: check the service.' }
    throw
}
