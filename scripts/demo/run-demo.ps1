<#
.SYNOPSIS
    Records the IB Tools demo video: takes the Revit test lock, builds + deploys, starts Revit 2023
    on a fresh copy of the demo model, lets drive.py press the buttons while rec.py records the
    screen, releases the lock (closes only our Revit) and cuts the mp4 with cut.py.
.EXAMPLE
    .\scripts\demo\run-demo.ps1                 # full run -> docs\demo\ibtools-demo.mp4
    .\scripts\demo\run-demo.ps1 -CutOnly        # re-cut the last take without Revit
#>
param(
    [switch]$CutOnly,
    # quay model with RD coordinates and 40 piles (the earlier test copy has no piles)
    [string]$Source = "C:\Development\VibeModel\demo_out\video\kade\Prinsengracht kademuur 2.0002.rvt"
)
$ErrorActionPreference = "Stop"
$repo = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$demoDir = Join-Path $env:LOCALAPPDATA "IBTools\demo"
$take = Join-Path $demoDir "take"
$model = Join-Path $demoDir "IB Tools demo - Prinsengracht kademuur.rvt"
$out = Join-Path $repo "docs\demo\ibtools-demo.mp4"
$stills = Join-Path $repo "docs\demo\stills"
if (-not $env:IBTOOLS_LOCK_OWNER) { $env:IBTOOLS_LOCK_OWNER = "demo-video@Revit-projectlijnen" }

if (-not $CutOnly) {
    New-Item -ItemType Directory -Force $demoDir | Out-Null
    if (Test-Path $take) { Remove-Item -Recurse -Force $take }
    New-Item -ItemType Directory -Force $take | Out-Null
    Copy-Item -Force $Source $model                      # fresh copy every run (the demo modifies it)
    Get-ChildItem $demoDir -Filter "*.0*.rvt" -ErrorAction SilentlyContinue | Remove-Item -Force

    Push-Location $repo
    try {
        & .\build.ps1 -OpenFile $model
        if ($LASTEXITCODE -ne 0) { throw "build.ps1 exit $LASTEXITCODE (2 = testslot bezet)" }
        $pidLine = Get-Content "$env:LOCALAPPDATA\IBTools\revit.pid" -TotalCount 1
        $revitPid = [int]($pidLine -split " ")[0]
        Write-Host "Demo: Revit pid $revitPid, take $take"
        & python (Join-Path $PSScriptRoot "drive.py") $take $revitPid
        $driveExit = $LASTEXITCODE
    }
    finally {
        & .\scripts\revit-lock.ps1 release
        Pop-Location
    }
    if ($driveExit -ne 0) { Write-Warning "drive.py exit $driveExit - zie $take\drive.log (video wordt toch gesneden)" }
}

New-Item -ItemType Directory -Force (Split-Path $out) | Out-Null
& python (Join-Path $PSScriptRoot "cut.py") $take $out $stills
Write-Host "Video: $out"
