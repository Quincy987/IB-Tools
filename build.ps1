<#
.SYNOPSIS
    Build (and optionally deploy + start Revit) for the IB Tools Revit add-in.

.DESCRIPTION
    Other sessions on this machine run their own Revit (e.g. VibeModel). This script NEVER
    closes or kills a Revit it did not start itself.

    -NoDeploy   compile only: nothing copied, Revit untouched, no test lock needed.
                Default in CI ($env:CI / TF_BUILD / GITHUB_ACTIONS) and in git worktrees
                (chain/build checks), unless -Deploy is given.

    Without -NoDeploy (see docs/TESTING.md):
      1. take the Revit test lock (%LOCALAPPDATA%\IBTools\revit-test.lock); busy -> exit 2
      2. compile
      3. deploy dll + .addin to %APPDATA%\Autodesk\Revit\Addins\<version>\
         If the dll is in use: close only the Revit this tooling started earlier for the same
         owner (pid in %LOCALAPPDATA%\IBTools\revit.pid). If another Revit still holds it,
         the old dll is renamed aside (*.old-<time>; the running Revit keeps working) and
         the new one copied next to it. If even that fails -> exit 3, nothing touched.
      4. start Revit detached, record its pid (unless -NoRestart)
    The lock stays yours after this script: release it with
        .\scripts\revit-lock.ps1 release      (closes YOUR Revit, removes the lock)

    Exit codes: 0 ok, 1 build failed, 2 test lock busy, 3 deploy blocked.

.PARAMETER RevitVersion
    2022, 2023 (default), 2024 or 2025.

.EXAMPLE
    .\build.ps1 -NoDeploy                 # compile check, safe anytime
    .\build.ps1                           # Revit 2023: lock, build, deploy, start Revit
    .\build.ps1 -RevitVersion 2025 -NoDeploy
    .\build.ps1 -Debug                    # Revit started with PROJECTION_TOGGLE_DEBUG=1
    .\build.ps1 -OpenFile C:\temp\copy.rvt  # start Revit with a (copy of a) model
#>

param(
    [ValidateSet("2022", "2023", "2024", "2025")]
    [string]$RevitVersion = "2023",

    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",

    # Compile only.
    [switch]$NoDeploy,

    # Deploy even in CI / a git worktree (overrides the compile-only default there).
    [switch]$Deploy,

    # Deploy but do not start Revit.
    [switch]$NoRestart,

    # Start Revit with debug logging (PROJECTION_TOGGLE_DEBUG=1).
    [switch]$Debug,

    # Open this .rvt/.rfa in the started Revit (test on a COPY of a model).
    [string]$OpenFile
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "scripts\ibtools-common.ps1")

$ProjectDir = Join-Path $PSScriptRoot "src\FoundationProjectionToggle"
$RevitExe = "C:\Program Files\Autodesk\Revit $RevitVersion\Revit.exe"
$AddinFolder = Join-Path $env:APPDATA "Autodesk\Revit\Addins\$RevitVersion"
$DllName = "FoundationProjectionToggle.dll"
$ManifestName = "FoundationProjectionToggle.addin"
$OutputDir = Join-Path $ProjectDir "bin\$Configuration\Revit$RevitVersion"

# --- Compile-only default in CI and git worktrees ---------------------------------------------
if (-not $NoDeploy -and -not $Deploy) {
    $reason = $null
    if ($env:CI -or $env:TF_BUILD -or $env:GITHUB_ACTIONS) {
        $reason = "CI"
    } else {
        # In a linked git worktree (bot/chain builds) ".git" is a file, not a folder.
        if (Test-Path -Path (Join-Path $PSScriptRoot ".git") -PathType Leaf) {
            $reason = "git worktree"
        }
    }
    if ($reason) {
        Write-Host "  ($reason gedetecteerd: alleen compileren. Gebruik -Deploy om toch te deployen.)" -ForegroundColor Gray
        $NoDeploy = $true
    }
}

$Owner = Get-IBToolsOwner

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  IB Tools build" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Target: Revit $RevitVersion ($Configuration)" -ForegroundColor Gray
if ($NoDeploy) {
    Write-Host "  Mode:   compile only (-NoDeploy)" -ForegroundColor Gray
} else {
    Write-Host "  Mode:   build + deploy$(if (-not $NoRestart) { ' + start Revit' })  [owner: $Owner]" -ForegroundColor Gray
}
Write-Host ""

# --- 1. Test lock ------------------------------------------------------------------------------
$lockTaken = $false
if (-not $NoDeploy) {
    Write-Host "[1/4] Revit test lock..." -ForegroundColor Yellow
    $lockResult = Enter-IBToolsLock -Owner $Owner
    if ($lockResult -eq "busy") {
        Write-Host ("    BEZET: {0}" -f (Format-IBToolsLock (Get-IBToolsLock))) -ForegroundColor Red
        Write-Host "    Niet gedeployed, Revit niet aangeraakt. Compileren kan wel: .\build.ps1 -NoDeploy" -ForegroundColor Gray
        exit 2
    }
    $lockTaken = ($lockResult -eq "taken")
    Write-Host ("    Slot {0} ({1})." -f $(if ($lockTaken) { "genomen" } else { "ververst" }), $Owner) -ForegroundColor Green
} else {
    Write-Host "[1/4] Test lock: niet nodig (compile only)." -ForegroundColor Gray
}

$succeeded = $false
try {
    # --- 2. Build ------------------------------------------------------------------------------
    Write-Host "[2/4] Building..." -ForegroundColor Yellow
    Push-Location $ProjectDir
    try {
        $buildOutput = & dotnet build -c $Configuration "-p:RevitVersion=$RevitVersion" -nologo 2>&1
        $buildExit = $LASTEXITCODE
    } finally {
        Pop-Location
    }
    if ($buildExit -ne 0) {
        Write-Host "    Build FAILED!" -ForegroundColor Red
        $buildOutput | ForEach-Object { Write-Host $_ }
        exit 1
    }
    $warnings = @($buildOutput | Where-Object { $_ -match ': warning ' } | Sort-Object -Unique)
    Write-Host ("    Build succeeded ({0} warning(s))." -f $warnings.Count) -ForegroundColor Green
    $warnings | ForEach-Object { Write-Host "    $_" -ForegroundColor DarkYellow }

    if ($NoDeploy) {
        Write-Host "[3/4] Deploy skipped (-NoDeploy)." -ForegroundColor Gray
        Write-Host "[4/4] Revit untouched." -ForegroundColor Gray
        Write-Host "    Output: $OutputDir" -ForegroundColor Gray
        $succeeded = $true
        exit 0
    }

    # --- 3. Deploy -----------------------------------------------------------------------------
    Write-Host "[3/4] Deploying to $AddinFolder ..." -ForegroundColor Yellow
    if (-not (Test-Path $AddinFolder)) { New-Item -ItemType Directory -Path $AddinFolder -Force | Out-Null }
    $targetDll = Join-Path $AddinFolder $DllName

    if (Test-IBToolsFileLocked $targetDll) {
        Write-Host "    $DllName is in gebruik." -ForegroundColor Yellow
        Write-Host ("    {0}" -f (Stop-IBToolsRevit -Owner $Owner)) -ForegroundColor Gray
        Start-Sleep -Milliseconds 500
    }
    if (Test-IBToolsFileLocked $targetDll) {
        # Held by a Revit we did not start: leave it alone, move the file aside instead.
        $aside = "$targetDll.old-$(Get-Date -Format 'yyyyMMddHHmmss')"
        try {
            Move-Item -Path $targetDll -Destination $aside -ErrorAction Stop
            Write-Host "    Andere Revit houdt de dll vast: oude dll opzij gezet ($([IO.Path]::GetFileName($aside)))." -ForegroundColor Yellow
            Write-Host "    Die Revit blijft de oude versie gebruiken tot hij opnieuw start." -ForegroundColor Gray
        } catch {
            $holders = @(Get-Process -Name Revit -ErrorAction SilentlyContinue | ForEach-Object { $_.Id }) -join ", "
            Write-Host "    GEBLOKKEERD: dll vergrendeld door een andere Revit (pid $holders) en niet te verplaatsen." -ForegroundColor Red
            Write-Host "    Niets gedeployed, geen Revit aangeraakt." -ForegroundColor Gray
            exit 3
        }
    }

    Copy-Item -Path (Join-Path $OutputDir $DllName) -Destination $AddinFolder -Force
    Copy-Item -Path (Join-Path $PSScriptRoot $ManifestName) -Destination $AddinFolder -Force

    # Remove old set-aside dlls that are no longer loaded anywhere.
    Get-ChildItem -Path $AddinFolder -Filter "$DllName.old-*" -ErrorAction SilentlyContinue | ForEach-Object {
        if (-not (Test-IBToolsFileLocked $_.FullName)) {
            Remove-Item $_.FullName -Force -ErrorAction SilentlyContinue
        }
    }
    Write-Host ("    Deployed: {0:yyyy-MM-dd HH:mm:ss}" -f (Get-Item $targetDll).LastWriteTime) -ForegroundColor Green

    # --- 4. Start Revit ------------------------------------------------------------------------
    if ($NoRestart) {
        Write-Host "[4/4] Revit niet gestart (-NoRestart)." -ForegroundColor Gray
    } elseif (-not (Test-Path $RevitExe)) {
        Write-Host "[4/4] Revit $RevitVersion niet gevonden: $RevitExe" -ForegroundColor Red
    } else {
        $existing = Get-IBToolsRevitProcess
        $rec = Get-IBToolsRevitRecord
        if ($existing -and $rec.Owner -eq $Owner) {
            Write-Host "[4/4] Eigen test-Revit (pid $($existing.Id)) draait al met de oude dll; afsluiten en opnieuw starten..." -ForegroundColor Yellow
            Write-Host ("    {0}" -f (Stop-IBToolsRevit -Owner $Owner)) -ForegroundColor Gray
        } else {
            Write-Host "[4/4] Starting Revit $RevitVersion..." -ForegroundColor Yellow
        }
        if ($Debug) {
            $env:PROJECTION_TOGGLE_DEBUG = "1"
            Write-Host "    Debug logging ENABLED ($env:LOCALAPPDATA\FoundationProjectionToggle\logs\)" -ForegroundColor Magenta
        }
        if ($OpenFile) {
            $proc = Start-Process -FilePath $RevitExe -ArgumentList ('"{0}"' -f (Resolve-Path $OpenFile).Path) -PassThru
        } else {
            $proc = Start-Process -FilePath $RevitExe -PassThru
        }
        Save-IBToolsRevitRecord -Process $proc -Owner $Owner -Version $RevitVersion
        Write-Host "    Revit gestart: pid $($proc.Id) (vastgelegd in $script:IBToolsPidFile)" -ForegroundColor Green
    }

    Write-Host ""
    Write-Host "  Klaar met testen? .\scripts\revit-lock.ps1 release" -ForegroundColor Cyan
    Write-Host "  (sluit alleen jouw Revit pid en geeft het slot vrij)" -ForegroundColor Gray
    Write-Host ""
    $succeeded = $true
    exit 0
} finally {
    # A lock taken in THIS run is given back when we stop on an error (exit inside try still
    # runs this block). On success the lock stays yours until revit-lock.ps1 release.
    if ($lockTaken -and -not $succeeded) {
        Exit-IBToolsLock -Owner $Owner | Out-Null
        Write-Host "    (slot weer vrijgegeven na fout)" -ForegroundColor Gray
    }
}
