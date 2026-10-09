<#
.SYNOPSIS
    Runs every Calling Bell check in order and prints one summary.

.DESCRIPTION
    1. Database: Phase 2 tables present (or -ApplyDatabase runs database\scripts\RunAll.sql first).
    2. Unit tests: CallingBell.Seo.Tests and CallingBell.Onboarding.Tests.
    3. Web build: tsc type check + Vite build.
    4. API checks: chat, team, video (tests\api\phase2_api.py).
    5. Phone checks: OTP sign-in and booking notifications for -Phone (tests\api\phone_flow.py).
    6. Browser checks: two signed-in people, live chat, team, booking dialog, a WebRTC video call (tests\e2e\phase2.cjs).
    7. Cleanup: tests\sql\CleanupTestData.sql removes everything the tests created.
    Steps 4-6 need the API (and step 6 the web app) running.

.EXAMPLE
    .\tests\RunAllTests.ps1 -Phone 98XXXXXXXX
.EXAMPLE
    .\tests\RunAllTests.ps1 -Phone 98XXXXXXXX -Interactive -SkipE2E      # real SMS/WhatsApp: type the code you get
.EXAMPLE
    .\tests\RunAllTests.ps1 -Api http://localhost:5081 -Web http://localhost:5174 -SkipBuild
#>
[CmdletBinding()]
param(
    [string]$Phone = $env:CB_TEST_PHONE,
    [string]$Api = 'http://localhost:5080',
    [string]$Web = 'http://localhost:5173',
    [string]$SqlServer = '.\SQLEXPRESS',
    [string]$Database = 'CallingBell',
    [switch]$Interactive,
    [switch]$ApplyDatabase,
    [switch]$SkipUnit,
    [switch]$SkipBuild,
    [switch]$SkipE2E,
    [switch]$SkipCleanup
)

$ErrorActionPreference = 'Continue'
$root = Split-Path -Parent $PSScriptRoot
$tests = $PSScriptRoot
$env:CB_API = $Api.TrimEnd('/')
$env:CB_WEB = $Web.TrimEnd('/')
$env:CB_SQL = $SqlServer
$env:CB_DB = $Database
$env:PYTHONIOENCODING = 'utf-8'
$summary = New-Object System.Collections.Generic.List[object]

function Add-Result([string]$Step, [string]$Result, [string]$Detail = '') {
    $summary.Add([pscustomobject]@{ Step = $Step; Result = $Result; Detail = $Detail })
}

function Invoke-Step([string]$Step, [scriptblock]$Body) {
    Write-Host "`n=============== $Step ===============" -ForegroundColor Cyan
    $started = Get-Date
    & $Body 2>&1 | ForEach-Object { "$_" } | Tee-Object -Variable lines | Write-Host
    $code = $LASTEXITCODE
    $seconds = [int]((Get-Date) - $started).TotalSeconds
    $tally = ($lines | Select-String -Pattern '(\d+/\d+ passed.*|Passed!.*|Failed!.*|built in .*)' | Select-Object -Last 1)
    $detail = if ($tally) { $tally.Matches[0].Value.Trim() } else { '' }
    Add-Result $Step ($(if ($code -eq 0) { 'PASS' } else { 'FAIL' })) "$detail ($seconds s)".Trim()
}

function Test-Url([string]$Url) {
    try { $null = Invoke-WebRequest -Uri $Url -UseBasicParsing -TimeoutSec 10; return $true } catch { return $false }
}

# ---------- 1. database ----------
if ($ApplyDatabase) {
    Invoke-Step 'Database scripts (RunAll.sql)' {
        Push-Location (Join-Path $root 'database\scripts')
        sqlcmd -S $SqlServer -E -C -I -b -d $Database -i RunAll.sql
        Pop-Location
    }
}
Invoke-Step 'Database: Phase 2 tables' {
    $q = "SET NOCOUNT ON; SELECT COUNT(*) FROM sys.tables WHERE name IN ('Conversations','ChatMessages','BusinessStaff','BusinessStaffServices','BusinessStaffHours','VideoRooms');"
    $n = (sqlcmd -S $SqlServer -E -C -I -b -d $Database -h -1 -W -Q $q | Select-Object -First 1).Trim()
    "$n/6 tables present"
    if ($n -ne '6') { 'Run database\scripts\31_ChatStaffVideo.sql (or pass -ApplyDatabase).'; $global:LASTEXITCODE = 1 } else { $global:LASTEXITCODE = 0 }
}

# ---------- 2. unit tests ----------
if (-not $SkipUnit) {
    # --artifacts-path keeps the build away from bin\Debug, which a running API locks.
    $artifacts = Join-Path $env:TEMP 'cb-test-artifacts'
    foreach ($project in 'CallingBell.Seo.Tests', 'CallingBell.Onboarding.Tests') {
        Invoke-Step "Unit tests: $project" { dotnet test (Join-Path $root "backend\tests\$project") --artifacts-path $artifacts -v q --nologo }
    }
}

# ---------- 3. web build ----------
if (-not $SkipBuild) {
    Invoke-Step 'Web build (type check + Vite)' { Push-Location (Join-Path $root 'web'); npm run build; Pop-Location }
}

# ---------- 4-6. against the running app ----------
if (-not (Test-Url "$env:CB_API/health")) {
    Add-Result 'API checks' 'SKIP' "API not reachable at $env:CB_API"
} else {
    Invoke-Step 'API: chat, team, video' { python (Join-Path $tests 'api\phase2_api.py') }

    if ($Phone) {
        $phoneArgs = @((Join-Path $tests 'api\phone_flow.py'), '--phone', $Phone)
        if ($Interactive) { $phoneArgs += '--interactive' }
        if ($Interactive) {
            # Interactive needs the console for the code prompt, so no output capture here.
            Write-Host "`n=============== Phone: OTP + notifications ===============" -ForegroundColor Cyan
            python @phoneArgs
            Add-Result 'Phone: OTP + notifications' ($(if ($LASTEXITCODE -eq 0) { 'PASS' } else { 'FAIL' })) 'interactive'
        } else {
            Invoke-Step 'Phone: OTP + notifications' { python @phoneArgs }
        }
    } else {
        Add-Result 'Phone: OTP + notifications' 'SKIP' 'pass -Phone (or set CB_TEST_PHONE)'
    }

    if ($SkipE2E) {
        Add-Result 'Browser: chat, team, video' 'SKIP' '-SkipE2E'
    } elseif (-not (Test-Url $env:CB_WEB)) {
        Add-Result 'Browser: chat, team, video' 'SKIP' "web app not reachable at $env:CB_WEB"
    } else {
        if (-not (Test-Path (Join-Path $tests 'node_modules\playwright-core'))) {
            Push-Location $tests; npm install --no-audit --no-fund | Out-Null; Pop-Location
        }
        Invoke-Step 'Browser: chat, team, video' { node (Join-Path $tests 'e2e\phase2.cjs') }
    }
}

# ---------- 7. cleanup ----------
if (-not $SkipCleanup) {
    Invoke-Step 'Cleanup test data' { sqlcmd -S $SqlServer -E -C -I -b -d $Database -i (Join-Path $tests 'sql\CleanupTestData.sql') }
}

Write-Host "`n=============== Summary ===============" -ForegroundColor Cyan
$summary | Format-Table -AutoSize | Out-String -Width 200 | Write-Host
$failed = @($summary | Where-Object Result -eq 'FAIL').Count
if ($failed) { Write-Host "$failed step(s) failed." -ForegroundColor Red; exit 1 }
Write-Host 'All steps passed.' -ForegroundColor Green
exit 0
