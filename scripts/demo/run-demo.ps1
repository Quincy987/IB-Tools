<#
.SYNOPSIS
    Records the IB Tools demo video: takes the Revit test lock, builds + deploys, starts Revit 2023
    on a fresh copy of the demo model, lets drive.py press the buttons while rec.py records the
    screen, releases the lock (closes only our Revit) and cuts the mp4 with cut.py.
.EXAMPLE
    .\scripts\demo\run-demo.ps1                     # full run -> docs\demo\ibtools-demo.mp4
    .\scripts\demo\run-demo.ps1 -Scene remmingwerk  # -> src\IBTools\Tools\Remmingwerk\demo\remmingwerk-demo.mp4
    .\scripts\demo\run-demo.ps1 -Scene paalcoordinaten  # -> src\IBTools\Tools\Paalcoordinaten\demo\paalcoordinaten-demo.mp4
    .\scripts\demo\run-demo.ps1 -CutOnly            # re-cut the last take without Revit
#>
param(
    [switch]$CutOnly,
    # "ibtools" = the three-button tour (ibtools-demo.mp4); "remmingwerk" and "paalcoordinaten"
    # (the automatic update: question on open, move, copy) = a separate video next to that tool
    [ValidateSet("ibtools", "remmingwerk", "paalcoordinaten")][string]$Scene = "ibtools",
    # quay model with RD coordinates and 40 piles (the earlier test copy has no piles)
    [string]$Source = "C:\Development\VibeModel\demo_out\video\kade\Prinsengracht kademuur 2.0002.rvt"
)
$ErrorActionPreference = "Stop"
$repo = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$demoDir = Join-Path $env:LOCALAPPDATA "IBTools\demo"
$take = Join-Path $demoDir "take-$Scene"
$model = Join-Path $demoDir "IB Tools demo - Prinsengracht kademuur.rvt"
# The tour of several tools lives in docs\demo; a single-tool video lives next to its tool.
if ($Scene -eq "ibtools") {
    $out = Join-Path $repo "docs\demo\ibtools-demo.mp4"
    $stills = Join-Path $repo "docs\demo\stills"
} else {
    $toolFolder = @{ remmingwerk = "Remmingwerk"; paalcoordinaten = "Paalcoordinaten" }[$Scene]
    $toolDemo = Join-Path $repo "src\IBTools\Tools\$toolFolder\demo"
    $out = Join-Path $toolDemo "$Scene-demo.mp4"
    $stills = Join-Path $toolDemo "stills"
}
if (-not $env:IBTOOLS_LOCK_OWNER) { $env:IBTOOLS_LOCK_OWNER = "demo-video@Revit-projectlijnen" }

if (-not $CutOnly) {
    New-Item -ItemType Directory -Force $demoDir | Out-Null
    if (Test-Path $take) { Remove-Item -Recurse -Force $take }
    New-Item -ItemType Directory -Force $take | Out-Null
    Copy-Item -Force $Source $model                      # fresh copy every run (the demo modifies it)
    Get-ChildItem $demoDir -Filter "*.0*.rvt" -ErrorAction SilentlyContinue | Remove-Item -Force

    # The paalcoordinaten scene shows the question on open, which build.ps1 switches off for the
    # Revit it starts unless the caller asks for it.
    $oldPileCheck = $env:IBTOOLS_PILECHECK
    if ($Scene -eq "paalcoordinaten") { $env:IBTOOLS_PILECHECK = "1" }
    Push-Location $repo
    try {
        & .\build.ps1 -Deploy -OpenFile $model      # -Deploy: also from a git worktree (compile-only there by default)
        if ($LASTEXITCODE -ne 0) { throw "build.ps1 exit $LASTEXITCODE (2 = testslot bezet)" }
        $pidLine = Get-Content "$env:LOCALAPPDATA\IBTools\revit.pid" -TotalCount 1
        $revitPid = [int]($pidLine -split " ")[0]
        Write-Host "Demo: Revit pid $revitPid, take $take"
        & python (Join-Path $PSScriptRoot "drive.py") $take $revitPid $Scene
        $driveExit = $LASTEXITCODE
    }
    finally {
        & .\scripts\revit-lock.ps1 release
        Pop-Location
        $env:IBTOOLS_PILECHECK = $oldPileCheck
    }
    if ($driveExit -ne 0) { Write-Warning "drive.py exit $driveExit - zie $take\drive.log (video wordt toch gesneden)" }
}

New-Item -ItemType Directory -Force (Split-Path $out) | Out-Null
& python (Join-Path $PSScriptRoot "cut.py") $take $out $stills
Write-Host "Video: $out"
