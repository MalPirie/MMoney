# Dump the current logcat buffer to a file and print the StripPager trace.
#   .\scripts\logs.ps1                 # StripPager lines only, saved + printed
#   .\scripts\logs.ps1 -All            # full buffer (still saved); prints StripPager + jank
#   .\scripts\logs.ps1 -Out my.log     # choose the output path
#   .\scripts\logs.ps1 -Follow         # stream StripPager lines live (Ctrl+C to stop)
param(
    [string]$Serial,
    [string]$Out,
    [switch]$All,
    [switch]$Follow
)

. "$PSScriptRoot\_common.ps1"

$adb = Get-Adb
$device = Get-Device -Serial $Serial

if ($Follow) {
    Write-Host "Streaming StripPager logs from $device (Ctrl+C to stop)..." -ForegroundColor Cyan
    & $adb -s $device logcat -v time | Select-String -Pattern 'StripPager','Skipped \d+ frames'
    return
}

if (-not $Out) {
    $logDir = Join-Path $RepoRoot 'scripts\logs'
    if (-not (Test-Path $logDir)) { New-Item -ItemType Directory -Path $logDir | Out-Null }
    $Out = Join-Path $logDir ("strippager-{0:yyyyMMdd-HHmmss}.log" -f (Get-Date))
}

Write-Host "Dumping logcat from $device -> $Out" -ForegroundColor Cyan
& $adb -s $device logcat -d -v time *:V > $Out

$trace = Get-Content $Out | Where-Object { $_ -match 'StripPager' }
Write-Host "--- StripPager ($($trace.Count) lines) ---" -ForegroundColor Green
$trace | ForEach-Object { ($_ -replace '^(\d\S*\s+\d\S*).*\[StripPager\] ', '$1  ') }

if ($All) {
    $jank = Get-Content $Out | Where-Object { $_ -match 'Skipped \d+ frames' }
    if ($jank) {
        Write-Host "--- Choreographer jank ---" -ForegroundColor Yellow
        $jank | ForEach-Object { ($_ -replace '.*Choreographer','Choreographer') }
    }
}

Write-Host "Saved full buffer to $Out" -ForegroundColor Green
