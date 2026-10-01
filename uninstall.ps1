<#
  Removes the PEAK mod menu from your game folder.

  Usage:
    .\uninstall.ps1
    .\uninstall.ps1 -PeakDir "D:\Games\PEAK"
#>
param(
    [string]$PeakDir
)

$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'find-peak.ps1')

if (Get-Process PEAK -ErrorAction SilentlyContinue) { throw 'PEAK is running. Close it first.' }

if ($PeakDir) {
    if (-not (Test-PeakDir $PeakDir)) { throw "No PEAK.exe in $PeakDir." }
    $targets = @($PeakDir)
} else {
    Write-Host 'Looking for PEAK (Steam libraries, then every drive)...'
    # Clean every install that has the mod, in case PEAK is installed in more than one place.
    $targets = @(Find-PeakDir | Where-Object { Test-Path (Join-Path $_ 'Doorstop.dll') })
    if ($targets.Count -eq 0) {
        $any = @(Find-PeakDir)
        if ($any.Count -gt 0) { Write-Host "PEAK found at $($any -join ', '), but the mod isn't installed there. Nothing to remove." }
        else { throw 'Could not find PEAK. Re-run with -PeakDir "C:\path\to\PEAK".' }
        return
    }
}

foreach ($dir in $targets) {
    foreach ($f in 'winhttp.dll', 'doorstop_config.ini', 'Doorstop.dll', 'PeakMenu.log') {
        Remove-Item (Join-Path $dir $f) -ErrorAction SilentlyContinue
    }
    Write-Host "Removed the mod from $dir" -ForegroundColor Green
}
