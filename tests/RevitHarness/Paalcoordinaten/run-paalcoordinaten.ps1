<#
.SYNOPSIS
    Revit integration test for the automatic Paalcoordinaten update, end to end (see
    tests/RevitHarness/README.md): takes the Revit test lock FIRST (busy = exit 2, nothing
    touched), deploys IB Tools + the harness, starts Revit 2023 with debug logging and a copy of
    the IB template, lets PaalcoordinatenHarness run cases A-G (ui-test-paalcoordinaten.ps1
    clicks "Nu bijwerken" for G2), then case H: closes its own Revit, starts Revit once more
    WITHOUT the IB Tools manifest (put back once that Revit's harness is done) and opens the saved model. Once the lock is ours it ALWAYS
    releases it (closes only its own Revit), puts the IB Tools manifest back and removes the
    harness from the Addins folder.
.EXAMPLE
    $env:IBTOOLS_LOCK_OWNER = "<thread name>"; .\tests\RevitHarness\Paalcoordinaten\run-paalcoordinaten.ps1
#>
param(
    [string]$Template = "K:\ALG\Autodesk\2023\REVIT_BEHEER\IB_template\Templates\IB_NLCS_2023_template.rte",
    [string]$PileFamily = "K:\ALG\Autodesk\2023\REVIT\Revit bibliotheek\Funderingspalen\NLI_Funderingspaal.rfa",
    [int]$TimeoutMinutes = 30,
    # Skip case H (second Revit start without IB Tools).
    [switch]$SkipH
)
$ErrorActionPreference = "Stop"
$repo = Resolve-Path (Join-Path $PSScriptRoot "..\..\..")
$root = Join-Path $env:LOCALAPPDATA "IBTools\test\paalcoordinaten"
$job = Join-Path $root "job"
$jobH = Join-Path $root "job-h"
$cache = Join-Path $env:LOCALAPPDATA "IBTools\paalcoordinaten-src"
$addins = Join-Path $env:APPDATA "Autodesk\Revit\Addins\2023"
$ibManifest = Join-Path $addins "FoundationProjectionToggle.addin"
$ibManifestAside = Join-Path $root "FoundationProjectionToggle.addin.aside"
$revitExe = "C:\Program Files\Autodesk\Revit 2023\Revit.exe"

# --- the lock first: nothing is touched before it is ours ---
Push-Location $repo
& .\scripts\revit-lock.ps1 take
if ($LASTEXITCODE -ne 0) {
    Pop-Location
    Write-Host "Testslot bezet door een andere sessie: niets aangeraakt. Probeer het over ~10 minuten opnieuw." -ForegroundColor Yellow
    exit 2
}
. (Join-Path $repo "scripts\ibtools-common.ps1")
$owner = Get-IBToolsOwner
$oldDebug = $env:IBTOOLS_DEBUG
$oldPileCheck = $env:IBTOOLS_PILECHECK
$ui = $null
$manifestMoved = $false
try {
    # --- job folders, template and pile family (K:, else the local cache) ---
    foreach ($d in $job, $jobH) { if (Test-Path $d) { Remove-Item -Recurse -Force $d } }
    New-Item -ItemType Directory -Force $job, $jobH, $cache | Out-Null
    foreach ($src in $Template, $PileFamily) {
        $name = Split-Path $src -Leaf
        if (Test-Path $src) { Copy-Item -Force $src (Join-Path $cache $name) }
        if (-not (Test-Path (Join-Path $cache $name))) { throw "niet gevonden: $src (en niet in $cache)" }
    }
    Copy-Item (Join-Path $cache (Split-Path $Template -Leaf)) (Join-Path $job "IB_template.rte")
    Copy-Item (Join-Path $cache (Split-Path $Template -Leaf)) (Join-Path $job "start.rte")
    Copy-Item (Join-Path $cache (Split-Path $PileFamily -Leaf)) $job
    Get-ChildItem $job -Recurse | ForEach-Object { if (-not $_.PSIsContainer) { $_.IsReadOnly = $false } }
    Set-Content -Path (Join-Path $job "paalcoordinaten.txt") -Value "paalcoordinaten"

    # --- harness ---
    & dotnet build (Join-Path $repo "tests\RevitHarness") -c Release -v q -nologo | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "harness build mislukt" }
    $harnessDll = Join-Path $repo "tests\RevitHarness\bin\Release\IBTools.RevitHarness.dll"
    $harnessManifest = Join-Path $repo "tests\RevitHarness\IBTools.RevitHarness.addin"
    Copy-Item -Force $harnessDll, $harnessManifest $addins
    $env:IBTOOLS_HARNESS_JOB = $job
    & .\build.ps1 -Deploy -Debug -OpenFile (Join-Path $job "start.rte")
    if ($LASTEXITCODE -ne 0) { throw "build.ps1 exit $LASTEXITCODE (2 = testslot bezet, 3 = deploy geblokkeerd)" }
    $revitPid = (Get-IBToolsRevitRecord).Pid
    Write-Host "Revit pid $revitPid, job $job"

    # Start-up dialogs that come before the harness is loaded (unsigned add-ins, other add-ins' errors).
    # UI Automation Invoke does not press these Win32 buttons; BM_CLICK on the button hwnd does.
    Add-Type @"
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
public static class IbDialogs {
    delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc f, IntPtr l);
    [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr p, EnumProc f, IntPtr l);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
    static string Text(IntPtr h) { var s = new StringBuilder(256); GetWindowText(h, s, 256); return s.ToString(); }
    static string Cls(IntPtr h) { var s = new StringBuilder(256); GetClassName(h, s, 256); return s.ToString(); }
    public static List<string> Answer(int revitPid) {
        var done = new List<string>();
        EnumWindows((h, l) => {
            uint pid; GetWindowThreadProcessId(h, out pid);
            if (pid != revitPid || !IsWindowVisible(h) || Cls(h) != "#32770") return true;
            var title = Text(h);
            string[] want = title.Contains("Unsigned") || title.StartsWith("Security") ? new[] { "Always Load" }
                : (title.EndsWith("- Error") || title.EndsWith("- Fout")) ? new[] { "Sluiten", "Close", "OK" } : null;
            if (want == null) return true;
            EnumChildWindows(h, (c, l2) => {
                if (Cls(c) != "Button") return true;
                var t = Text(c).Replace("&", "");
                if (Array.IndexOf(want, t) < 0) return true;
                SendMessage(c, 0x00F5, IntPtr.Zero, IntPtr.Zero); // BM_CLICK
                done.Add(title + " -> " + t);
                return false;
            }, IntPtr.Zero);
            return true;
        }, IntPtr.Zero);
        return done;
    }
}
"@

    # Waits for done.txt in $folder while answering start-up dialogs; removes the harness manifest
    # as soon as the harness is loaded (started.txt) and runs $onStarted once.
    function Wait-Harness([string]$folder, [int]$procId, [scriptblock]$onStarted) {
        $deadline = (Get-Date).AddMinutes($TimeoutMinutes)
        $started = $false
        while (-not (Test-Path (Join-Path $folder "done.txt"))) {
            if ((Get-Date) -gt $deadline) { throw "geen done.txt in $folder binnen $TimeoutMinutes minuten" }
            if (-not (Get-Process -Id $procId -ErrorAction SilentlyContinue)) { throw "Revit (pid $procId) is gestopt" }
            foreach ($d in [IbDialogs]::Answer($procId)) { Write-Host "  dialoog $d" }
            if (-not $started -and (Test-Path (Join-Path $folder "started.txt"))) {
                Remove-Item (Join-Path $addins "IBTools.RevitHarness.addin") -ErrorAction SilentlyContinue
                $started = $true
                if ($onStarted) { & $onStarted }
            }
            Start-Sleep -Seconds 2
        }
        Write-Host ("Harnas klaar ($folder): " + (Get-Content (Join-Path $folder "done.txt")))
    }

    Wait-Harness $job $revitPid {
        $script:ui = Start-Process powershell -PassThru -WindowStyle Hidden -ArgumentList @(
            "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", (Join-Path $PSScriptRoot "ui-test-paalcoordinaten.ps1"),
            "-Job", $job, "-RevitPid", $revitPid)
    }

    # --- H: the saved model in a Revit without IB Tools (manifest aside only during start-up) ---
    if (-not $SkipH) {
        & .\scripts\revit-lock.ps1 stop | Out-Host       # our own Revit only; the lock stays ours
        Copy-Item (Join-Path $job "paal.rvt") (Join-Path $jobH "paal zonder IB Tools.rvt")
        Set-Content -Path (Join-Path $jobH "zonder-ibtools.txt") -Value "h"
        Copy-Item -Force $harnessDll, $harnessManifest $addins
        Move-Item -Force $ibManifest $ibManifestAside
        $manifestMoved = $true
        $env:IBTOOLS_HARNESS_JOB = $jobH
        $procH = Start-Process -FilePath $revitExe -ArgumentList ('"{0}"' -f (Join-Path $jobH "paal zonder IB Tools.rvt")) -PassThru
        Save-IBToolsRevitRecord -Process $procH -Owner $owner -Version "2023"
        Write-Host "Revit zonder IB Tools: pid $($procH.Id)"
        # The IB Tools manifest only comes back after the harness is done: Revit 2023 reads
        # add-in manifests after the harness has started (seen: IB Tools started 9 s after the
        # harness when the manifest was put back at started.txt).
        Wait-Harness $jobH $procH.Id $null
        Move-Item -Force $ibManifestAside $ibManifest
        $manifestMoved = $false
    }
}
finally {
    if ($ui -and -not $ui.HasExited) { $ui.Kill() }
    & .\scripts\revit-lock.ps1 release
    if ($manifestMoved -and (Test-Path $ibManifestAside)) { Move-Item -Force $ibManifestAside $ibManifest }
    Remove-Item (Join-Path $addins "IBTools.RevitHarness.addin") -ErrorAction SilentlyContinue
    Remove-Item (Join-Path $addins "IBTools.RevitHarness.dll") -ErrorAction SilentlyContinue
    Remove-Item Env:\IBTOOLS_HARNESS_JOB -ErrorAction SilentlyContinue
    $env:IBTOOLS_DEBUG = $oldDebug
    $env:IBTOOLS_PILECHECK = $oldPileCheck
    Pop-Location
}
Write-Host ""
Get-Content (Join-Path $job "results.txt") | Where-Object { $_ -match " (OK|FOUT) " }
if (Test-Path (Join-Path $jobH "results.txt")) { Get-Content (Join-Path $jobH "results.txt") | Where-Object { $_ -match " (OK|FOUT) " } }
