# Clear the device logcat buffer (and bump it to 16M so bursts aren't dropped).
#   .\scripts\clear-logs.ps1 [-Serial <serial>]
param([string]$Serial)

. "$PSScriptRoot\_common.ps1"

$adb = Get-Adb
$device = Get-Device -Serial $Serial
& $adb -s $device logcat -G 16M 2>$null
& $adb -s $device logcat -c
Write-Host "logcat cleared on $device (buffer 16M)." -ForegroundColor Green
