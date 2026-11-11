# Shared helpers for build.ps1 and scripts/revit-lock.ps1 (dot-source this file).
# Windows PowerShell 5.1 compatible.
#
# State lives in %LOCALAPPDATA%\IBTools\ :
#   revit-test.lock  one line "<owner> <UTC time>"  -> who is testing in Revit right now
#   revit.pid        one line "<pid> <start ticks UTC> <owner> <version>" -> the Revit this
#                    tooling started itself. Only that process is ever closed by the scripts.

$script:IBToolsStateDir = Join-Path $env:LOCALAPPDATA "IBTools"
$script:IBToolsLockFile = Join-Path $script:IBToolsStateDir "revit-test.lock"
$script:IBToolsPidFile  = Join-Path $script:IBToolsStateDir "revit.pid"
$script:IBToolsLockMaxAge = [TimeSpan]::FromHours(2)

function Initialize-IBToolsStateDir {
    if (-not (Test-Path $script:IBToolsStateDir)) {
        New-Item -ItemType Directory -Path $script:IBToolsStateDir -Force | Out-Null
    }
}

# Who "we" are: $env:IBTOOLS_LOCK_OWNER, else "<git branch>@<repo folder>". No spaces.
function Get-IBToolsOwner {
    if ($env:IBTOOLS_LOCK_OWNER) {
        return ($env:IBTOOLS_LOCK_OWNER -replace '\s+', '_')
    }
    $root = Split-Path -Parent $PSScriptRoot
    if (-not (Test-Path (Join-Path $root ".git"))) { $root = $PSScriptRoot }
    $branch = $null
    try {
        $branch = (& git -C $root rev-parse --abbrev-ref HEAD 2>$null)
    } catch { }
    if (-not $branch) { $branch = "unknown" }
    $folder = Split-Path -Leaf $root
    return (("{0}@{1}" -f $branch, $folder) -replace '\s+', '_')
}

# Returns $null when there is no lock, else an object with Owner, TimeUtc, Age, IsStale.
function Get-IBToolsLock {
    if (-not (Test-Path $script:IBToolsLockFile)) { return $null }
    $line = ""
    try { $line = (Get-Content -Path $script:IBToolsLockFile -TotalCount 1 -ErrorAction Stop) } catch { }
    if ($null -eq $line) { $line = "" }
    $parts = $line.Trim() -split '\s+'
    $owner = if ($parts.Count -ge 1 -and $parts[0]) { $parts[0] } else { "(onbekend)" }
    $time = $null
    if ($parts.Count -ge 2) {
        try {
            $time = [DateTime]::ParseExact($parts[1], "yyyy-MM-ddTHH:mm:ssZ",
                [Globalization.CultureInfo]::InvariantCulture,
                [Globalization.DateTimeStyles]::AssumeUniversal -bor [Globalization.DateTimeStyles]::AdjustToUniversal)
        } catch { $time = $null }
    }
    if ($null -eq $time) {
        $time = (Get-Item $script:IBToolsLockFile).LastWriteTimeUtc
    }
    $age = [DateTime]::UtcNow - $time
    return [pscustomobject]@{
        Owner   = $owner
        TimeUtc = $time
        Age     = $age
        IsStale = ($age -ge $script:IBToolsLockMaxAge)
    }
}

function Format-IBToolsLock($lock) {
    if ($null -eq $lock) { return "vrij" }
    $state = if ($lock.IsStale) { "VERLOPEN" } else { "bezet" }
    return ("{0} door {1} sinds {2:yyyy-MM-dd HH:mm} UTC ({3:N0} min)" -f $state, $lock.Owner, $lock.TimeUtc, $lock.Age.TotalMinutes)
}

# Take (or refresh) the test lock. Returns "taken", "refreshed" or "busy".
function Enter-IBToolsLock([string]$Owner, [switch]$Force) {
    Initialize-IBToolsStateDir
    $lock = Get-IBToolsLock
    if ($lock -and -not $lock.IsStale -and $lock.Owner -ne $Owner -and -not $Force) {
        return "busy"
    }
    $result = if ($lock -and $lock.Owner -eq $Owner -and -not $lock.IsStale) { "refreshed" } else { "taken" }
    $stamp = [DateTime]::UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", [Globalization.CultureInfo]::InvariantCulture)
    Set-Content -Path $script:IBToolsLockFile -Value ("{0} {1}" -f $Owner, $stamp) -Encoding ASCII
    return $result
}

# Remove the lock if it is ours (or stale, or -Force). Returns $true if no lock remains.
function Exit-IBToolsLock([string]$Owner, [switch]$Force) {
    $lock = Get-IBToolsLock
    if ($null -eq $lock) { return $true }
    if ($lock.Owner -eq $Owner -or $lock.IsStale -or $Force) {
        Remove-Item -Path $script:IBToolsLockFile -Force -ErrorAction SilentlyContinue
        return -not (Test-Path $script:IBToolsLockFile)
    }
    return $false
}

# --- Revit started by this tooling -----------------------------------------------------------

function Get-IBToolsRevitRecord {
    if (-not (Test-Path $script:IBToolsPidFile)) { return $null }
    $line = (Get-Content -Path $script:IBToolsPidFile -TotalCount 1 -ErrorAction SilentlyContinue)
    if (-not $line) { return $null }
    $p = $line.Trim() -split '\s+'
    if ($p.Count -lt 2) { return $null }
    return [pscustomobject]@{
        Pid        = [int]$p[0]
        StartTicks = [long]$p[1]
        Owner      = if ($p.Count -ge 3) { $p[2] } else { "(onbekend)" }
        Version    = if ($p.Count -ge 4) { $p[3] } else { "" }
    }
}

# The live Revit process described by the pid file, or $null (gone / pid reused by another exe).
function Get-IBToolsRevitProcess {
    $rec = Get-IBToolsRevitRecord
    if ($null -eq $rec) { return $null }
    $proc = Get-Process -Id $rec.Pid -ErrorAction SilentlyContinue
    if ($null -eq $proc -or $proc.ProcessName -ne "Revit") { return $null }
    try {
        $delta = [Math]::Abs(($proc.StartTime.ToUniversalTime().Ticks - $rec.StartTicks))
        if ($delta -gt [TimeSpan]::FromSeconds(5).Ticks) { return $null }
    } catch {
        return $null
    }
    return $proc
}

function Save-IBToolsRevitRecord([System.Diagnostics.Process]$Process, [string]$Owner, [string]$Version) {
    Initialize-IBToolsStateDir
    $old = Get-IBToolsRevitRecord
    $oldProc = Get-IBToolsRevitProcess
    if ($oldProc -and $old.Owner -ne $Owner) {
        Write-Host ("    LET OP: een eerder door {0} gestarte test-Revit (pid {1}) draait nog; die wordt niet aangeraakt." -f $old.Owner, $old.Pid) -ForegroundColor Yellow
    }
    $ticks = $Process.StartTime.ToUniversalTime().Ticks
    Set-Content -Path $script:IBToolsPidFile -Value ("{0} {1} {2} {3}" -f $Process.Id, $ticks, $Owner, $Version) -Encoding ASCII
}

# Close the Revit this tooling started, but only if it was started by $Owner (or -AnyOwner).
# Never touches any other Revit process. Returns a short status string.
function Stop-IBToolsRevit([string]$Owner, [switch]$AnyOwner, [int]$GraceSeconds = 30) {
    $rec = Get-IBToolsRevitRecord
    $proc = Get-IBToolsRevitProcess
    if ($null -eq $proc) {
        if ($rec) { Remove-Item $script:IBToolsPidFile -Force -ErrorAction SilentlyContinue }
        return "geen eigen Revit actief"
    }
    if ($rec.Owner -ne $Owner -and -not $AnyOwner) {
        return ("eigen-gestarte Revit pid {0} is van {1}, niet van {2}: niet aangeraakt" -f $rec.Pid, $rec.Owner, $Owner)
    }

    Write-Host ("    Revit pid {0} (gestart door {1}) netjes afsluiten..." -f $proc.Id, $rec.Owner) -ForegroundColor Yellow
    try { $proc.CloseMainWindow() | Out-Null } catch { }
    if (-not $proc.WaitForExit($GraceSeconds * 1000)) {
        Write-Host ("    Reageert niet binnen {0}s, pid {1} geforceerd stoppen." -f $GraceSeconds, $proc.Id) -ForegroundColor Red
        Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue
        $proc.WaitForExit(15000) | Out-Null
    }
    Remove-Item $script:IBToolsPidFile -Force -ErrorAction SilentlyContinue
    return ("Revit pid {0} afgesloten" -f $proc.Id)
}

# True if the file exists and cannot be opened exclusively (e.g. loaded by a running Revit).
function Test-IBToolsFileLocked([string]$Path) {
    if (-not (Test-Path $Path)) { return $false }
    try {
        $fs = [System.IO.File]::Open($Path, 'Open', 'ReadWrite', 'None')
        $fs.Close()
        return $false
    } catch {
        return $true
    }
}
