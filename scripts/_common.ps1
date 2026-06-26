# Shared helpers for the StripPager dev scripts.
# Dot-source this from the other scripts: . "$PSScriptRoot\_common.ps1"

$ErrorActionPreference = 'Stop'

$Script:RepoRoot = Split-Path -Parent $PSScriptRoot
$Script:AppProject = Join-Path $RepoRoot 'MMoney.App\MMoney.App.csproj'
$Script:AndroidFramework = 'net10.0-android'

function Get-Adb {
    # Prefer adb on PATH; fall back to the common Windows SDK location.
    $cmd = Get-Command adb -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }

    $candidates = @(
        (Join-Path $env:LOCALAPPDATA 'Android\Sdk\platform-tools\adb.exe'),
        'C:\Program Files (x86)\Android\android-sdk\platform-tools\adb.exe',
        (Join-Path $env:ANDROID_HOME 'platform-tools\adb.exe'),
        (Join-Path $env:ANDROID_SDK_ROOT 'platform-tools\adb.exe')
    ) | Where-Object { $_ -and (Test-Path $_) }

    $first = @($candidates) | Select-Object -First 1
    if ($first) { return $first }
    throw "adb not found. Install platform-tools or set ANDROID_HOME."
}

function Get-Device {
    param([string]$Serial)
    $adb = Get-Adb
    if ($Serial) { return $Serial }

    $lines = & $adb devices | Select-Object -Skip 1 | Where-Object { $_ -match '\tdevice$' }
    $serials = @($lines | ForEach-Object { ($_ -split '\t')[0] })
    if ($serials.Count -eq 0) { throw "No authorised device/emulator attached (check 'adb devices')." }
    if ($serials.Count -gt 1) { Write-Host "Multiple devices; using $($serials[0]). Pass -Serial to choose." -ForegroundColor Yellow }
    return $serials[0]
}

function Get-AndroidSdkRoot {
    $candidates = @($env:ANDROID_HOME, $env:ANDROID_SDK_ROOT,
        (Join-Path $env:LOCALAPPDATA 'Android\Sdk'),
        'C:\Program Files (x86)\Android\android-sdk') | Where-Object { $_ -and (Test-Path $_) }
    return (@($candidates) | Select-Object -First 1)
}

# Ensure the MSBuild Android targets can find the SDK.
$sdk = Get-AndroidSdkRoot
if ($sdk) { $env:ANDROID_HOME = $sdk; $env:ANDROID_SDK_ROOT = $sdk }
