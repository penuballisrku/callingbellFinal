<#
  Installs the IP geolocation databases:
    - country.mmdb  the visitor's country code shown next to the logo (default)
    - city.mmdb     the visitor's district, used to pre-select their city and list its areas in the city dropdown (-City)

  Default (no account needed): DB-IP "IP to Country/City Lite" (CC BY 4.0 - the site shows the required
  "IP geolocation by DB-IP" credit automatically). Updated monthly.

  Optional: MaxMind GeoLite2 Country/City instead. Create a free account at https://www.maxmind.com/en/geolite2/signup,
  generate a licence key, then set MAXMIND_ACCOUNT_ID and MAXMIND_LICENSE_KEY before running.

  Run from the repo root, then restart the API (re-run monthly to keep the data current):
      powershell -File database/tools/download_geoip.ps1
      powershell -File database/tools/download_geoip.ps1 -City

  The .mmdb file is git-ignored: neither licence allows committing it to the repository.
#>
param(
  [switch]$City,
  [string]$AccountId = $env:MAXMIND_ACCOUNT_ID,
  [string]$LicenseKey = $env:MAXMIND_LICENSE_KEY,
  [string]$Destination
)
$ErrorActionPreference = 'Stop'
$edition = if ($City) { 'City' } else { 'Country' }
if (-not $Destination) {
  $Destination = Join-Path $PSScriptRoot "..\..\backend\src\CallingBell.Api\App_Data\$($edition.ToLowerInvariant()).mmdb"
}
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$work = Join-Path ([IO.Path]::GetTempPath()) ("geoip-" + [guid]::NewGuid())
New-Item -ItemType Directory -Path $work | Out-Null
try {
  if ($AccountId -and $LicenseKey) {
    Write-Host "Downloading MaxMind GeoLite2 $edition..."
    $archive = Join-Path $work "GeoLite2-$edition.tar.gz"
    $pair = [Convert]::ToBase64String([Text.Encoding]::ASCII.GetBytes("${AccountId}:${LicenseKey}"))
    Invoke-WebRequest -Uri "https://download.maxmind.com/geoip/databases/GeoLite2-$edition/download?suffix=tar.gz" `
      -Headers @{ Authorization = "Basic $pair" } -OutFile $archive -UseBasicParsing
    tar -xzf $archive -C $work
    $mmdb = (Get-ChildItem -Path $work -Recurse -Filter "GeoLite2-$edition.mmdb" | Select-Object -First 1).FullName
    if (-not $mmdb) { throw "GeoLite2-$edition.mmdb not found in the download." }
  }
  else {
    Write-Host "Downloading DB-IP IP to $edition Lite (no account needed)..."
    $gz = Join-Path $work 'db.mmdb.gz'
    $downloaded = $false
    # This month's edition, falling back to last month's early in the month.
    foreach ($month in @((Get-Date), (Get-Date).AddMonths(-1))) {
      $url = "https://download.db-ip.com/free/dbip-$($edition.ToLowerInvariant())-lite-$($month.ToString('yyyy-MM')).mmdb.gz"
      try { Invoke-WebRequest -Uri $url -OutFile $gz -UseBasicParsing; $downloaded = $true; Write-Host "  $url"; break } catch { }
    }
    if (-not $downloaded) { throw "Could not download the DB-IP $edition Lite database. Check your internet connection." }
    $mmdb = Join-Path $work 'db.mmdb'
    $in = [IO.File]::OpenRead($gz)
    try {
      $gzip = New-Object IO.Compression.GZipStream($in, [IO.Compression.CompressionMode]::Decompress)
      $out = [IO.File]::Create($mmdb)
      try { $gzip.CopyTo($out) } finally { $out.Dispose(); $gzip.Dispose() }
    } finally { $in.Dispose() }
  }

  New-Item -ItemType Directory -Force -Path (Split-Path $Destination) | Out-Null
  Copy-Item $mmdb $Destination -Force
  $size = [math]::Round((Get-Item $Destination).Length / 1MB, 1)
  Write-Host "Saved $size MB to $((Resolve-Path $Destination).Path). Restart the API to load it."
}
finally {
  Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
}
