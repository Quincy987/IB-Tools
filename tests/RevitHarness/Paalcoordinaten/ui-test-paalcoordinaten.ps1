<#
.SYNOPSIS
    UI half of the Paalcoordinaten harness (case G2): waits for ui-ready.txt, finds the
    open-check question "IB Tools - Paalcoordinaten" of the test Revit (a Win32 task dialog),
    takes a screenshot, clicks "Nu bijwerken" (BM_CLICK on the command link: it has no UI
    Automation Invoke pattern), then reads the report window "Paalcoordinaten" (summary +
    details), takes a screenshot, closes it and writes ui-done.txt.
.EXAMPLE
    .\ui-test-paalcoordinaten.ps1 -Job C:\...\job -RevitPid 12345
#>
param(
    [Parameter(Mandatory = $true)][string]$Job,
    [Parameter(Mandatory = $true)][int]$RevitPid,
    [int]$TimeoutSeconds = 900
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing, System.Windows.Forms
Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class IbClick { [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l); }
"@
$AE = [System.Windows.Automation.AutomationElement]
$TS = [System.Windows.Automation.TreeScope]
$out = Join-Path $Job "ui-result.txt"
# Titles with the o-umlaut, written as a char so this file can stay ASCII (PowerShell 5.1).
$oe = [char]0x00F6
$QuestionTitle = "IB Tools - Paalco${oe}rdinaten"   # Revit puts the add-in name in front
$ReportTitle = "Paalco${oe}rdinaten"

function Say([string]$s) { Add-Content -Path $out -Value ((Get-Date -Format "HH:mm:ss") + " " + $s) -Encoding UTF8 }

function Find-Top([string]$title, [string]$requiredId, [int]$seconds) {
    $deadline = (Get-Date).AddSeconds($seconds)
    $cond = New-Object System.Windows.Automation.PropertyCondition($AE::ProcessIdProperty, $RevitPid)
    while ((Get-Date) -lt $deadline) {
        foreach ($top in $AE::RootElement.FindAll($TS::Children, $cond)) {
            $candidates = @($top) + @($top.FindAll($TS::Children,
                (New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::Window))))
            foreach ($w in $candidates) {
                if ($w.Current.Name -ne $title) { continue }
                if ($requiredId) {
                    $probe = $w.FindFirst($TS::Descendants,
                        (New-Object System.Windows.Automation.PropertyCondition($AE::AutomationIdProperty, $requiredId)))
                    if (-not $probe) { continue }
                }
                return $w
            }
        }
        Start-Sleep -Milliseconds 500
    }
    return $null
}

function Get-ById($win, [string]$id) {
    return $win.FindFirst($TS::Descendants, (New-Object System.Windows.Automation.PropertyCondition($AE::AutomationIdProperty, $id)))
}

function Shot($win, [string]$file) {
    try {
        $r = $win.Current.BoundingRectangle
        $bmp = New-Object System.Drawing.Bitmap ([int]$r.Width), ([int]$r.Height)
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        $g.CopyFromScreen([int]$r.X, [int]$r.Y, 0, 0, $bmp.Size)
        $bmp.Save((Join-Path $Job $file), [System.Drawing.Imaging.ImageFormat]::Png)
        $g.Dispose(); $bmp.Dispose()
        Say "Screenshot: $file"
    } catch { Say "Screenshot mislukt: $($_.Exception.Message)" }
}

$q = $null
try {
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while (-not (Test-Path (Join-Path $Job "ui-ready.txt"))) {
        if ((Get-Date) -gt $deadline) { throw "ui-ready.txt kwam niet" }
        Start-Sleep -Seconds 2
    }

    $q = Find-Top $QuestionTitle $null 180
    if (-not $q) { throw "Vraag '$QuestionTitle' niet gevonden" }
    Start-Sleep -Milliseconds 800
    $texts = $q.FindAll($TS::Descendants, [System.Windows.Automation.Condition]::TrueCondition) |
        ForEach-Object { $_.Current.Name } | Where-Object { $_ } | Select-Object -Unique
    Say ("Vraag: " + ($texts -join " | "))
    Shot $q "ui-vraag.png"

    # Task dialog command links are Win32 child windows (class CCCommandLink, UIA type Pane).
    $all = $q.FindAll($TS::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
    $link = $all | Where-Object { $_.Current.Name -like "Nu bijwerken*" } | Select-Object -First 1
    if (-not $link -or $link.Current.NativeWindowHandle -eq 0) { throw ("'Nu bijwerken' niet gevonden; elementen: " + (($all | ForEach-Object { $_.Current.Name }) -join " | ")) }
    [IbClick]::PostMessage([IntPtr]$link.Current.NativeWindowHandle, 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null  # BM_CLICK
    Say "'Nu bijwerken' geklikt"

    $res = Find-Top $ReportTitle "DetailsBox" 180
    if (-not $res) { throw "Rapportvenster niet gevonden" }
    Start-Sleep -Milliseconds 800
    Say ("Rapport: " + (Get-ById $res "SummaryText").Current.Name)
    $details = Get-ById $res "DetailsBox"
    try { Say ("Details:`r`n" + $details.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value) } catch { }
    Shot $res "ui-rapport.png"
    (Get-ById $res "CloseButton").GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Say "Rapport gesloten"
}
catch {
    Say ("UI-TEST FOUT: " + $_.Exception.Message)
    # A question left open is modal: Revit would never idle again and the harness could not
    # report. Close it (= "Niet nu"), so the harness finishes with FOUT instead of hanging.
    try {
        if ($q -and $q.Current.NativeWindowHandle -ne 0) {
            [IbClick]::PostMessage([IntPtr]$q.Current.NativeWindowHandle, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null  # WM_CLOSE
            Say "Vraag gesloten na de fout"
        }
    } catch { }
}
finally {
    Set-Content -Path (Join-Path $Job "ui-done.txt") -Value "done"
}
