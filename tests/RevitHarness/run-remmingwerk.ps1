<#
.SYNOPSIS
    Revit integration test for the "Remmingwerk" button, end to end (see README.md):
    takes the Revit test lock (via build.ps1), deploys IB Tools + the harness, opens a COPY of the
    kade model, answers the start-up dialogs, runs ui-test-remmingwerk.ps1 and waits for the
    harness to finish. ALWAYS releases the lock (closes only its own Revit) and removes the
    harness from the Addins folder again.
.EXAMPLE
    $env:IBTOOLS_LOCK_OWNER = "<thread name>"; .\tests\RevitHarness\run-remmingwerk.ps1
#>
param(
    [string]$Source = "C:\Development\VibeModel\demo_out\video\kade\Prinsengracht kademuur 2.0002.rvt",
    [int]$TimeoutMinutes = 30
)
$ErrorActionPreference = "Stop"
$repo = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$root = Join-Path $env:LOCALAPPDATA "IBTools\test\remmingwerk"
$job = Join-Path $root "job"
$model = Join-Path $root "Remmingwerk test.rvt"
$addins = Join-Path $env:APPDATA "Autodesk\Revit\Addins\2023"
$lib = "K:\ALG\Autodesk\2023\REVIT\Revit bibliotheek"
$cache = Join-Path $env:LOCALAPPDATA "IBTools\remmingwerk-src\staal"
$steel = @(
    "Strucural beams\NLRS_28_SF_balken_staal\NLRS_28_SF_LIB_balk HEA_gen_ISR",
    "Structural columns\NLRS_28_SC_Kolommen_staal\NLRS_28_SC_TLB_kolom HEA_gen_ISR",
    "Structural columns\NLRS_28_SC_Kolommen_staal\NLRS_28_SC_TLB_kolom buis_gen_ISR")

# --- job folder, model copy, steel families (K:, else the local cache) ---
if (Test-Path $job) { Remove-Item -Recurse -Force $job }
New-Item -ItemType Directory -Force (Join-Path $job "staal") | Out-Null
Copy-Item -Force $Source $model
Get-ChildItem $root -Filter "*.0*.rvt" -ErrorAction SilentlyContinue | Remove-Item -Force
foreach ($s in $steel) {
    $name = Split-Path $s -Leaf
    $from = Join-Path $lib $s
    if (-not (Test-Path $from)) { $from = Join-Path $cache $name }
    Copy-Item -Recurse -Force $from (Join-Path $job "staal\$name")
}
Set-Content -Path (Join-Path $job "remmingwerk.txt") -Value "remmingwerk"

# --- harness ---
& dotnet build (Join-Path $repo "tests\RevitHarness") -c Release -v q -nologo | Out-Host
if ($LASTEXITCODE -ne 0) { throw "harness build mislukt" }
$harnessDll = Join-Path $repo "tests\RevitHarness\bin\Release\IBTools.RevitHarness.dll"
Copy-Item -Force $harnessDll, (Join-Path $repo "tests\RevitHarness\IBTools.RevitHarness.addin") $addins

$env:IBTOOLS_HARNESS_JOB = $job
Push-Location $repo
try {
    & .\build.ps1 -Deploy -OpenFile $model
    if ($LASTEXITCODE -ne 0) { throw "build.ps1 exit $LASTEXITCODE (2 = testslot bezet, 3 = deploy geblokkeerd)" }
    $revitPid = [int]((Get-Content "$env:LOCALAPPDATA\IBTools\revit.pid" -TotalCount 1) -split " ")[0]
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
    function Answer-Dialogs { foreach ($d in [IbDialogs]::Answer($revitPid)) { Write-Host "  dialoog $d" } }

    $deadline = (Get-Date).AddMinutes($TimeoutMinutes)
    $harnessGone = $false
    $ui = $null
    while (-not (Test-Path (Join-Path $job "done.txt"))) {
        if ((Get-Date) -gt $deadline) { throw "geen done.txt binnen $TimeoutMinutes minuten" }
        if (-not (Get-Process -Id $revitPid -ErrorAction SilentlyContinue)) { throw "Revit (pid $revitPid) is gestopt" }
        Answer-Dialogs
        if (-not $harnessGone -and (Test-Path (Join-Path $job "started.txt"))) {
            # Loaded: take the manifest away so other Revit sessions never load the harness.
            Remove-Item (Join-Path $addins "IBTools.RevitHarness.addin") -ErrorAction SilentlyContinue
            $harnessGone = $true
            $ui = Start-Process powershell -PassThru -WindowStyle Hidden -ArgumentList @(
                "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", (Join-Path $PSScriptRoot "ui-test-remmingwerk.ps1"),
                "-Job", $job, "-RevitPid", $revitPid)
        }
        Start-Sleep -Seconds 2
    }
    Write-Host ("Harnas klaar: " + (Get-Content (Join-Path $job "done.txt")))
}
finally {
    if ($ui -and -not $ui.HasExited) { $ui.Kill() }
    & .\scripts\revit-lock.ps1 release
    Remove-Item (Join-Path $addins "IBTools.RevitHarness.addin") -ErrorAction SilentlyContinue
    Remove-Item (Join-Path $addins "IBTools.RevitHarness.dll") -ErrorAction SilentlyContinue
    Remove-Item Env:\IBTOOLS_HARNESS_JOB -ErrorAction SilentlyContinue
    Pop-Location
}
Get-Content (Join-Path $job "results.txt") | Select-Object -Last 15
