# Build, install and launch the app on the device, clearing logcat first.
#   .\scripts\deploy.ps1 [-Serial <serial>] [-Clean]
# -Clean wipes obj/bin first. Use it if the app crashes on launch with
#   "No view found for id 0x... for fragment NavigationRootManager_ElementBasedFragment"
# — that's a stale resource table from incremental builds, not a code bug.
param([string]$Serial, [switch]$Clean)

. "$PSScriptRoot\_common.ps1"

$adb = Get-Adb
$device = Get-Device -Serial $Serial

if ($Clean) {
    Write-Host "Wiping obj/bin (clean build)..." -ForegroundColor Cyan
    Get-ChildItem -Path $RepoRoot -Recurse -Directory -Include obj, bin -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch '\\Mobiorum\.Material3\.Tests\\' } |
        ForEach-Object { Remove-Item -Recurse -Force $_.FullName -ErrorAction SilentlyContinue }
    & $adb -s $device uninstall com.mobiorum.mmoney 2>&1 | Out-Null
}

Write-Host "Clearing logcat on $device..." -ForegroundColor Cyan
& $adb -s $device logcat -G 16M 2>$null
& $adb -s $device logcat -c

Write-Host "Build + install + launch on $device..." -ForegroundColor Cyan
dotnet build $AppProject -f $AndroidFramework -t:Run -p:AdbTarget="-s $device" --nologo -v q
if ($LASTEXITCODE -ne 0) { throw "Deploy failed (exit $LASTEXITCODE)." }

Start-Sleep -Milliseconds 500
$running = & $adb -s $device shell pidof com.mobiorum.mmoney
if ($running) { Write-Host "Running (pid $running) on $device." -ForegroundColor Green }
else { Write-Host "Deployed; app not detected as running yet." -ForegroundColor Yellow }
