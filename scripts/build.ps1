# Build the Android app (compiles Mobiorum.Material3 + MMoney.App). No device needed.
#   .\scripts\build.ps1
param([switch]$Quiet)

. "$PSScriptRoot\_common.ps1"

$verbosity = if ($Quiet) { 'q' } else { 'm' }
Write-Host "Building $AppProject ($AndroidFramework)..." -ForegroundColor Cyan
dotnet build $AppProject -f $AndroidFramework --nologo -v $verbosity
if ($LASTEXITCODE -ne 0) { throw "Build failed (exit $LASTEXITCODE)." }
Write-Host "Build OK." -ForegroundColor Green
