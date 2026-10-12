<#
  Rebuilds the web app and the API and redeploys them to the production service on this machine (C:\CallingBell, served on
  http://127.0.0.1:8080 and published as https://www.callinbell.com through the Cloudflare Tunnel). Run from an elevated PowerShell.

  Settings and secrets are in the service's environment (HKLM:\SYSTEM\CurrentControlSet\Services\CallingBell, value Environment),
  not in files, so a redeploy never overwrites them. New database scripts: run RunAll.sql against CallingBell_Prod (-SqlScripts).
#>
param([switch]$SqlScripts)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent

Push-Location "$repo\web"
# Not npm ci: it deletes node_modules first, which fails half-way (EPERM) while the Vite dev server holds native modules open
# and leaves the dev server broken. npm install only touches what changed.
npm install --no-audit --no-fund; if ($LASTEXITCODE) { throw 'npm install failed (a file in node_modules is locked? stop the Vite dev server and retry)' }
npm run build; if ($LASTEXITCODE) { throw 'web build failed' }
Pop-Location
dotnet publish "$repo\backend\src\CallingBell.Api" -c Release -o "$env:TEMP\cb-publish" --nologo
if ($LASTEXITCODE) { throw 'API publish failed' }

if ($SqlScripts) {
    Push-Location "$repo\database\scripts"
    sqlcmd -S .\SQLEXPRESS -E -C -I -f 65001 -b -d CallingBell_Prod -i RunAll.sql; if ($LASTEXITCODE) { throw 'database scripts failed' }
    Pop-Location
}

Stop-Service CallingBell
robocopy "$env:TEMP\cb-publish" C:\CallingBell\api /E /NFL /NDL /NJH /NP /XD App_Data | Out-Null
robocopy "$repo\web\dist" C:\CallingBell\spa /MIR /NFL /NDL /NJH /NP | Out-Null
Start-Service CallingBell
Start-Sleep 5
(Invoke-WebRequest http://127.0.0.1:8080/health -UseBasicParsing).Content
