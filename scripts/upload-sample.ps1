# Generate a sample account and upload it to the device, then load it via the admin Import.
#   .\scripts\upload-sample.ps1 [-Serial <serial>]
#
# Produces a fresh sample account (see tools/SampleData) and pushes <id>.jsonl to the device's Downloads. Load it
# in the app: Settings (overflow menu) -> tap the About box 5x -> Admin -> Import -> pick the file -> Replace.
# The admin import adopts the sample's id and keeps the current account as a recoverable deleted account (ADR-0008).
param([string]$Serial)

. "$PSScriptRoot\_common.ps1"

$adb = Get-Adb
$device = Get-Device -Serial $Serial

$tool = Join-Path $RepoRoot 'tools\SampleData'
$outDir = Join-Path $env:TEMP 'mmoney-sample'
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

$id = [Guid]::NewGuid().ToString('N')
$out = Join-Path $outDir "$id.jsonl"

Write-Host "Generating sample account $id..." -ForegroundColor Cyan
dotnet run --project $tool -- $out
if ($LASTEXITCODE -ne 0) { throw "Sample generation failed (exit $LASTEXITCODE)." }

Write-Host "Pushing to device $device..." -ForegroundColor Cyan
& $adb -s $device push $out "/sdcard/Download/$id.jsonl" | Out-Null

Write-Host ""
Write-Host "Uploaded $id.jsonl to the device's Downloads." -ForegroundColor Green
Write-Host "Load it in the app:" -ForegroundColor Green
Write-Host "  overflow menu -> Settings -> tap the About box 5x -> Admin -> Import -> pick $id.jsonl -> Replace"
