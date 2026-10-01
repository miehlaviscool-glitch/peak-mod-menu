<#
  Shared PEAK locator, dot-sourced by install.ps1 and uninstall.ps1.

  Find-PeakDir returns every PEAK install it can find, best match first. It looks in:
    1. Steam's own records: the registry, then every library listed in libraryfolders.vdf
       (this covers libraries on any drive, in any folder name).
    2. A scan of every fixed/removable drive for  <folder>\steamapps\common\PEAK
       up to two folders deep (e.g. E:\Games\SteamLibrary, D:\Steam, F:\Games\Steam\Library).

  A folder only counts if it contains PEAK.exe.
#>

$script:PeakAppId = '3527290'

function Test-PeakDir([string]$Dir) {
    $Dir -and (Test-Path -LiteralPath (Join-Path $Dir 'PEAK.exe'))
}

# Steam install folders according to the registry.
function Get-SteamRoots {
    $roots = @()
    $keys = @(
        @{ Path = 'HKCU:\Software\Valve\Steam';                   Name = 'SteamPath' },
        @{ Path = 'HKLM:\SOFTWARE\WOW6432Node\Valve\Steam';       Name = 'InstallPath' },
        @{ Path = 'HKLM:\SOFTWARE\Valve\Steam';                   Name = 'InstallPath' }
    )
    foreach ($k in $keys) {
        $v = (Get-ItemProperty -Path $k.Path -ErrorAction SilentlyContinue).($k.Name)
        if ($v) { $roots += ($v -replace '/', '\') }
    }
    $roots | Where-Object { $_ } | Select-Object -Unique
}

# Every Steam library folder (one per drive/location the user added in Steam).
function Get-SteamLibraries {
    $libs = @()
    foreach ($root in Get-SteamRoots) {
        $libs += $root
        $vdf = Join-Path $root 'steamapps\libraryfolders.vdf'
        if (Test-Path -LiteralPath $vdf) {
            foreach ($line in Get-Content -LiteralPath $vdf -ErrorAction SilentlyContinue) {
                if ($line -match '^\s*"path"\s+"([^"]+)"') {
                    $libs += ($Matches[1] -replace '\\\\', '\')
                }
            }
        }
    }
    $libs | Where-Object { $_ } | Select-Object -Unique
}

# Where Steam says PEAK lives inside a library (uses the app manifest's installdir).
function Get-PeakCandidatesFromLibrary([string]$Library) {
    $steamapps = Join-Path $Library 'steamapps'
    $installdir = 'PEAK'
    $manifest = Join-Path $steamapps "appmanifest_$($script:PeakAppId).acf"
    if (Test-Path -LiteralPath $manifest) {
        $m = Select-String -LiteralPath $manifest -Pattern '"installdir"\s+"([^"]+)"' | Select-Object -First 1
        if ($m) { $installdir = $m.Matches[0].Groups[1].Value }
    }
    Join-Path $steamapps "common\$installdir"
}

# Scan all drives for <folder>[\<folder>]\steamapps\common\PEAK.
function Find-PeakByDriveScan {
    param([string[]]$Roots)   # normally every drive root; overridable so the scan can be tested

    if (-not $Roots) {
        $Roots = [IO.DriveInfo]::GetDrives() |
                 Where-Object { $_.IsReady -and $_.DriveType -in 'Fixed', 'Removable' } |
                 ForEach-Object { $_.RootDirectory.FullName }
    }
    $skip = @('Windows', '$Recycle.Bin', 'System Volume Information', 'ProgramData', 'AppData', 'node_modules')
    foreach ($root in $Roots) {
        $found = @(Join-Path $root 'steamapps\common\PEAK')
        $level1 = Get-ChildItem -LiteralPath $root -Directory -ErrorAction SilentlyContinue |
                  Where-Object { $skip -notcontains $_.Name }
        foreach ($d1 in $level1) {
            $found += Join-Path $d1.FullName 'steamapps\common\PEAK'
            $level2 = Get-ChildItem -LiteralPath $d1.FullName -Directory -ErrorAction SilentlyContinue |
                      Where-Object { $skip -notcontains $_.Name }
            foreach ($d2 in $level2) { $found += Join-Path $d2.FullName 'steamapps\common\PEAK' }
        }
        $found
    }
}

function Find-PeakDir {
    param([switch]$SkipSteamRecords, [switch]$SkipDriveScan)

    $candidates = @()
    if (-not $SkipSteamRecords) {
        foreach ($lib in Get-SteamLibraries) { $candidates += Get-PeakCandidatesFromLibrary $lib }
    }
    if (-not $SkipDriveScan) { $candidates += Find-PeakByDriveScan }

    # Windows paths are case-insensitive, so de-duplicate on a lower-cased full path.
    $seen = @{}
    foreach ($c in $candidates) {
        if (-not (Test-PeakDir $c)) { continue }
        $full = [IO.Path]::GetFullPath($c).TrimEnd('\')
        $key = $full.ToLowerInvariant()
        if (-not $seen.ContainsKey($key)) { $seen[$key] = $true; $full }
    }
}
