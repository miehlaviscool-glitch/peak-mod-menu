<#
  Builds the PEAK mod menu and installs it into your game folder.

  Usage:
    .\install.ps1                              # auto-detect PEAK on every drive
    .\install.ps1 -PeakDir "D:\Games\PEAK"     # or point at the install folder yourself
#>
param(
    [string]$PeakDir
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$doorstopVersion = '4.5.0'

. (Join-Path $root 'find-peak.ps1')

if (-not $PeakDir) {
    Write-Host 'Looking for PEAK (Steam libraries, then every drive)...'
    $found = @(Find-PeakDir)
    if ($found.Count -gt 1) {
        Write-Host 'Found more than one PEAK install:' -ForegroundColor Yellow
        $found | ForEach-Object { Write-Host "  $_" }
        Write-Host "Using the first one. Pass -PeakDir to choose a different one."
    }
    $PeakDir = $found | Select-Object -First 1
}
if (-not $PeakDir -or -not (Test-PeakDir $PeakDir)) {
    throw "Couldn't find PEAK. Re-run with -PeakDir ""C:\path\to\PEAK"" (Steam > right-click PEAK > Manage > Browse local files)."
}
if (-not (Test-Path (Join-Path $PeakDir 'PEAK_Data\Managed\Assembly-CSharp.dll'))) {
    throw "$PeakDir has PEAK.exe but no PEAK_Data\Managed\Assembly-CSharp.dll. Is the game fully installed? Try Steam > Verify integrity of game files."
}
Write-Host "PEAK found at: $PeakDir"

if (Get-Process PEAK -ErrorAction SilentlyContinue) {
    throw 'PEAK is running. Close the game and run this again.'
}

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw '.NET SDK not found. Install it with:  winget install Microsoft.DotNet.SDK.8'
}

# 1. Build the mod (the assembly is named Doorstop.dll, which is what the loader looks for).
Write-Host 'Building...'
dotnet build "$root\PeakMenu\PeakMenu.csproj" -c Release "-p:PeakDir=$PeakDir" --nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
$dll = Join-Path $root 'PeakMenu\bin\Release\netstandard2.1\Doorstop.dll'

# 2. Get the Unity Doorstop loader (winhttp.dll) from its official release.
$cache = Join-Path $root 'tools\doorstop'
if (-not (Test-Path "$cache\x64\winhttp.dll")) {
    Write-Host "Downloading Unity Doorstop $doorstopVersion..."
    New-Item -ItemType Directory -Force $cache | Out-Null
    $zip = Join-Path $cache 'doorstop.zip'
    $url = "https://github.com/NeighTools/UnityDoorstop/releases/download/v$doorstopVersion/doorstop_win_release_$doorstopVersion.zip"
    Invoke-WebRequest $url -OutFile $zip
    Expand-Archive $zip $cache -Force
    Remove-Item $zip
}

# 3. Install.
Copy-Item "$cache\x64\winhttp.dll" $PeakDir -Force
Copy-Item "$cache\x64\doorstop_config.ini" $PeakDir -Force
Copy-Item $dll $PeakDir -Force
Remove-Item (Join-Path $PeakDir 'PeakMenu.log') -ErrorAction SilentlyContinue

Write-Host ''
Write-Host 'Installed. Launch PEAK from Steam and press F1 in game.' -ForegroundColor Green
