<#
.SYNOPSIS
    Checks in a real Revit 2023 what the Windows download block does and that Installeren.cmd
    removes it. Takes the Revit test lock FIRST (busy = exit 2, nothing touched), backs up the
    IB Tools files in %APPDATA%\Autodesk\Revit\Addins\2023 and runs two scenarios from the 2023
    release zip, each with its own Revit (started and closed by this script only):
      a  "downloaded" files copied straight into the Addins folder (still blocked)
      b  the same files installed with Installeren.cmd (the real route for a user)
    Per scenario: does IB Tools start (its log says "Add-in startup completed successfully" with the
    release version), which dialogs did Revit show, what does the Revit journal say about the dll.
    Afterwards ALWAYS: close our Revit, release the lock, put the backed-up files back.
.EXAMPLE
    $env:IBTOOLS_LOCK_OWNER = "<thread name>"; .\tests\Release\revit-check-install.ps1
#>
param(
    [string]$ZipDir,
    [int]$WaitSeconds = 240
)
$ErrorActionPreference = "Stop"
$repo = Resolve-Path (Join-Path $PSScriptRoot "..\..")
. (Join-Path $repo "scripts\ibtools-common.ps1")
. (Join-Path $repo "scripts\release\ReleaseFiles.ps1")
. (Join-Path $PSScriptRoot "ReleaseTestHelpers.ps1")
$version = Get-IBToolsVersion $repo
if (-not $ZipDir) { $ZipDir = Join-Path $env:LOCALAPPDATA "IBTools\release\v$version" }
$zip = Join-Path $ZipDir "IBTools-v$version-Revit2023.zip"
if (-not (Test-Path $zip)) { throw "Zip niet gevonden: $zip (eerst .\scripts\release.ps1)" }
$revitExe = "C:\Program Files\Autodesk\Revit 2023\Revit.exe"
$addins = Join-Path $env:APPDATA "Autodesk\Revit\Addins\2023"
$journals = Join-Path $env:LOCALAPPDATA "Autodesk\Revit\Autodesk Revit 2023\Journals"
$files = @($IBToolsDllName, $IBToolsAddinName)
$owner = (Get-IBToolsOwner) -replace '\s+', '_'
$work = Join-Path $env:LOCALAPPDATA "IBTools\test\install-check"
$report = New-Object System.Collections.ArrayList


Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
Add-Type @"
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
public static class IbInstallDialogs {
    delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc f, IntPtr l);
    [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr p, EnumProc f, IntPtr l);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
    public static string Text(IntPtr h) { var s = new StringBuilder(512); GetWindowText(h, s, 512); return s.ToString(); }
    static string Cls(IntPtr h) { var s = new StringBuilder(256); GetClassName(h, s, 256); return s.ToString(); }
    public static List<IntPtr> Dialogs(int revitPid) {
        var list = new List<IntPtr>();
        EnumWindows((h, l) => {
            uint pid; GetWindowThreadProcessId(h, out pid);
            if (pid == revitPid && IsWindowVisible(h) && Cls(h) == "#32770") list.Add(h);
            return true;
        }, IntPtr.Zero);
        return list;
    }
    public static string Press(IntPtr dialog, string[] want) {
        string pressed = null;
        EnumChildWindows(dialog, (c, l2) => {
            if (Cls(c) != "Button") return true;
            var t = Text(c).Replace("&", "");
            if (Array.IndexOf(want, t) < 0) return true;
            SendMessage(c, 0x00F5, IntPtr.Zero, IntPtr.Zero); // BM_CLICK
            pressed = t;
            return false;
        }, IntPtr.Zero);
        return pressed;
    }
}
"@

function Get-DialogText([IntPtr]$h) {
    try {
        $el = [System.Windows.Automation.AutomationElement]::FromHandle($h)
        $texts = $el.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition) |
            ForEach-Object { $_.Current.Name } | Where-Object { $_ -and $_.Length -gt 3 } | Select-Object -Unique
        return (($texts -join " | ") -replace "\s+", " ")
    } catch { return "(tekst niet leesbaar)" }
}

function Invoke-Scenario([string]$Name, [scriptblock]$Prepare) {
    Write-Host "Scenario ${Name}:" -ForegroundColor Yellow
    foreach ($f in $files) { Remove-Item -LiteralPath (Join-Path $addins $f) -Force -ErrorAction SilentlyContinue }
    & $Prepare
    $blocked = @($files | Where-Object { Test-IBToolsBlocked (Join-Path $addins $_) })
    $state = if ($blocked.Count) { "geblokkeerd: " + ($blocked -join ", ") } else { "niet geblokkeerd" }
    Write-Host "  Addins\2023: $state"

    $log = Join-Path $env:LOCALAPPDATA ("IBTools\logs\log_{0:yyyy-MM-dd}.txt" -f (Get-Date))
    $logStart = if (Test-Path $log) { (Get-Item $log).Length } else { 0 }
    $started = Get-Date
    $env:IBTOOLS_DEBUG = "1"
    $proc = Start-Process -FilePath $revitExe -PassThru
    Save-IBToolsRevitRecord -Process $proc -Owner $owner -Version "2023"
    Write-Host "  Revit gestart: pid $($proc.Id)"

    $seen = @{}
    $dialogs = New-Object System.Collections.ArrayList
    $ok = $false
    $windowSince = $null
    $deadline = $started.AddSeconds($WaitSeconds)
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Seconds 2
        if ($proc.HasExited) { [void]$dialogs.Add("Revit is gestopt"); break }
        foreach ($h in [IbInstallDialogs]::Dialogs($proc.Id)) {
            $title = [IbInstallDialogs]::Text($h)
            $key = "$h"
            if (-not $seen.ContainsKey($key)) {
                $seen[$key] = $true
                $line = "$title :: $(Get-DialogText $h)"
                [void]$dialogs.Add($line)
                Write-Host "  dialoog: $line" -ForegroundColor Gray
            }
            $want = if ($title -match "Unsigned|^Security") { @("Always Load") } else { @("Sluiten", "Close", "OK") }
            [void][IbInstallDialogs]::Press($h, $want)
        }
        if (Test-Path $log) {
            $fs = [IO.File]::Open($log, 'Open', 'Read', 'ReadWrite')
            try { [void]$fs.Seek($logStart, 'Begin'); $new = (New-Object IO.StreamReader($fs)).ReadToEnd() } finally { $fs.Close() }
            if ($new -match "Add-in version: $([regex]::Escape($version))" -and $new -match "Add-in startup completed successfully") { $ok = $true }
        }
        $proc.Refresh()
        if ($proc.MainWindowTitle -like "Autodesk Revit 2023*") { if (-not $windowSince) { $windowSince = Get-Date } }
        # Done when IB Tools started, or the main window has been there for a minute without it.
        if ($ok -and $windowSince) { break }
        if ($windowSince -and ((Get-Date) - $windowSince).TotalSeconds -gt 60) { break }
    }
    Write-Host ("  " + (Stop-IBToolsRevit -Owner $owner))

    $journal = Get-ChildItem $journals -Filter "journal.*.txt" -ErrorAction SilentlyContinue |
        Where-Object { $_.LastWriteTime -gt $started } | Sort-Object LastWriteTime | Select-Object -Last 1
    $jl = @()
    if ($journal) {
        $jl = @(Select-String -LiteralPath $journal.FullName -Pattern "$([regex]::Escape($IBToolsDllName))|0x80131515|loadFromRemoteSources|network location" |
            ForEach-Object { $_.Line.Trim() } | Select-Object -First 6)
    }
    $entry = [pscustomobject]@{
        Scenario = $Name; Files = $state; IBToolsStarted = $ok
        Dialogs = ($dialogs -join " || "); Journal = $(if ($journal) { $journal.Name } else { "(geen)" }); JournalLines = ($jl -join " || ")
    }
    [void]$report.Add($entry)
    Write-Host ("  IB Tools gestart: {0}" -f $ok) -ForegroundColor $(if ($ok) { "Green" } else { "Red" })
    foreach ($l in $jl) { Write-Host "  journal: $l" -ForegroundColor Gray }
}

# The lock only now: everything above touches nothing, and from here on the finally below
# always releases it.
Push-Location $repo
& .\scripts\revit-lock.ps1 take
if ($LASTEXITCODE -ne 0) {
    Pop-Location
    Write-Host "Testslot bezet door een andere sessie: niets aangeraakt. Probeer het over ~10 minuten opnieuw." -ForegroundColor Yellow
    exit 2
}
$backup = Join-Path $work "backup"
$touched = $false
try {
    # Another Revit 2023 (not ours) holding the dll: leave everything alone.
    if (Test-IBToolsFileLocked (Join-Path $addins $files[0])) { throw "De dll in Addins\2023 is in gebruik door een andere Revit: niets gedaan." }
    if (Test-Path $work) { Remove-Item -Recurse -Force $work }
    New-Item -ItemType Directory -Force $backup | Out-Null
    foreach ($f in $files) {
        $p = Join-Path $addins $f
        if (Test-Path -LiteralPath $p) { Copy-Item -LiteralPath $p -Destination $backup }
    }
    $src = Expand-IBToolsDownload $zip (Join-Path $work "Downloads\IBTools-v$version-Revit2023")

    $touched = $true
    Invoke-Scenario "a (gedownloade bestanden zo in de Addins-map)" {
        foreach ($f in $files) { Copy-Item -LiteralPath (Join-Path $src $f) -Destination $addins }   # keeps the stream
    }
    Invoke-Scenario "b (via Installeren.cmd)" {
        $env:IBTOOLS_INSTALL_UNATTENDED = "1"
        try {
            $out = & cmd.exe /c ('""{0}""' -f (Join-Path $src "Installeren.cmd")) 2>&1 | Out-String
            $rc = $LASTEXITCODE
            Write-Host ("  Installeren.cmd exit {0}" -f $rc)
            Write-Host (($out -split "`r?`n" | Where-Object { $_.Trim() } | ForEach-Object { "    | $_" }) -join "`n") -ForegroundColor Gray
            if ($rc -ne 0) { throw "Installeren.cmd exit $rc" }
        } finally { Remove-Item Env:\IBTOOLS_INSTALL_UNATTENDED -ErrorAction SilentlyContinue }
    }
}
finally {
    & .\scripts\revit-lock.ps1 release
    if ($touched) {
        # Back to exactly what was there: the backed-up files, and nothing that was not there before.
        foreach ($f in $files) {
            $b = Join-Path $backup $f
            $p = Join-Path $addins $f
            if (Test-Path -LiteralPath $b) { Copy-Item -LiteralPath $b -Destination $addins -Force }
            else { Remove-Item -LiteralPath $p -Force -ErrorAction SilentlyContinue }
        }
        Write-Host "Addins\2023: oorspronkelijke IB Tools-bestanden teruggezet." -ForegroundColor Gray
    }
    Remove-Item Env:\IBTOOLS_DEBUG -ErrorAction SilentlyContinue
    Pop-Location
}

$report | Format-List | Out-Host
$a = $report | Where-Object { $_.Scenario -like "a*" }
$b = $report | Where-Object { $_.Scenario -like "b*" }
if ($a) { Write-Host ("Zonder deblokkeren: IB Tools gestart = {0}" -f $a.IBToolsStarted) }
if ($b -and $b.IBToolsStarted) { Write-Host "Installeren.cmd in Revit 2023: IB Tools start." -ForegroundColor Green; exit 0 }
Write-Host "Installeren.cmd in Revit 2023: IB Tools start NIET." -ForegroundColor Red
exit 1
