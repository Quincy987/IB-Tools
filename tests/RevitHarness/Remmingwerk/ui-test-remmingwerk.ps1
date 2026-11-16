<#
.SYNOPSIS
    UI half of the Remmingwerk harness: waits for ui-ready.txt, reads and confirms the
    "Remmingwerk" window that the harness opened with PostCommand, reads the result window,
    then undoes the placement with Ctrl+Z on the Revit main window (the Quick Access Toolbar
    Undo has no UI Automation Invoke pattern) and writes ui-done.txt. Uses Windows UI Automation (WPF controls are found by their x:Name).
.EXAMPLE
    .\ui-test-remmingwerk.ps1 -Job C:\...\job -RevitPid 12345
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
public static class IbWin { [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h); }
"@
$AE = [System.Windows.Automation.AutomationElement]
$Title = "Remmingwerk"
$out = Join-Path $Job "ui-result.txt"

function Say([string]$s) { Add-Content -Path $out -Value ((Get-Date -Format "HH:mm:ss") + " " + $s) -Encoding UTF8 }

function Find-Window([string]$requiredId, [int]$seconds) {
    $deadline = (Get-Date).AddSeconds($seconds)
    $cond = New-Object System.Windows.Automation.PropertyCondition($AE::ProcessIdProperty, $RevitPid)
    while ((Get-Date) -lt $deadline) {
        foreach ($top in $AE::RootElement.FindAll([System.Windows.Automation.TreeScope]::Children, $cond)) {
            $candidates = @($top) + @($top.FindAll([System.Windows.Automation.TreeScope]::Children,
                (New-Object System.Windows.Automation.PropertyCondition($AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::Window))))
            foreach ($w in $candidates) {
                if ($w.Current.Name -ne $Title) { continue }
                $probe = $w.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
                    (New-Object System.Windows.Automation.PropertyCondition($AE::AutomationIdProperty, $requiredId)))
                if ($probe) { return $w }
            }
        }
        Start-Sleep -Milliseconds 500
    }
    return $null
}

function Get-Element($win, [string]$id) {
    return $win.FindFirst([System.Windows.Automation.TreeScope]::Descendants,
        (New-Object System.Windows.Automation.PropertyCondition($AE::AutomationIdProperty, $id)))
}

function Get-Value($win, [string]$id) {
    $el = Get-Element $win $id
    if (-not $el) { return "<niet gevonden>" }
    try { return $el.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value } catch { }
    try { return $el.GetCurrentPattern([System.Windows.Automation.SelectionPattern]::Pattern).Current.GetSelection()[0].Current.Name } catch { }
    return $el.Current.Name
}

function Click($win, [string]$id) {
    (Get-Element $win $id).GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
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

function Shot-Screen([string]$file) {
    try {
        $b = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
        $bmp = New-Object System.Drawing.Bitmap $b.Width, $b.Height
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        $g.CopyFromScreen($b.X, $b.Y, 0, 0, $bmp.Size)
        $bmp.Save((Join-Path $Job $file), [System.Drawing.Imaging.ImageFormat]::Png)
        $g.Dispose(); $bmp.Dispose()
        Say "Screenshot: $file"
    } catch { Say "Screenshot mislukt: $($_.Exception.Message)" }
}

function Undo-InRevit {
    $main = [System.Diagnostics.Process]::GetProcessById($RevitPid).MainWindowHandle
    [IbWin]::SetForegroundWindow($main) | Out-Null
    Start-Sleep -Milliseconds 400
    [System.Windows.Forms.SendKeys]::SendWait("^z")
    Say "Undo geklikt (Ctrl+Z naar het Revit-venster)"
}

try {
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while (-not (Test-Path (Join-Path $Job "ui-ready.txt"))) {
        if ((Get-Date) -gt $deadline) { throw "ui-ready.txt kwam niet" }
        Start-Sleep -Seconds 2
    }

    $win = Find-Window "RowsBox" 120
    if (-not $win) { throw "Invulvenster niet gevonden" }
    Start-Sleep -Milliseconds 800
    Say ("Info: " + (Get-Element $win "InfoText").Current.Name)
    Say ("Level: " + (Get-Value $win "LevelBox"))
    foreach ($id in "WrijfgordingBox", "WrijfstijlBox", "GordingBox", "StijlBox", "PaalBox") { Say ("${id}: " + (Get-Value $win $id)) }
    foreach ($id in "RowsBox", "RowSpacingBox", "WrijfBreedteBox", "WrijfHoogteBox", "PileSpacingBox", "PileTipBox", "OverlengthBox") { Say ("${id}: " + (Get-Value $win $id)) }
    Say ("Overzicht: " + (Get-Element $win "OverviewText").Current.Name)
    Say ("Fout: " + (Get-Element $win "ErrorText").Current.Name)
    Shot $win "ui-window.png"
    Click $win "OkButton"
    Say "Plaatsen geklikt"

    $res = Find-Window "DetailsBox" 300
    if (-not $res) { throw "Resultaatvenster niet gevonden" }
    Say ("Resultaat: " + (Get-Element $res "SummaryText").Current.Name)
    Say ("Details:`r`n" + (Get-Value $res "DetailsBox"))
    Shot $res "ui-result.png"
    Click $res "CloseButton"
    Say "Resultaatvenster gesloten"
    Start-Sleep -Seconds 2
    Shot-Screen "ui-model-after.png"

    Undo-InRevit
    Start-Sleep -Seconds 3
}
catch {
    Say ("UI-TEST FOUT: " + $_.Exception.Message)
}
finally {
    Set-Content -Path (Join-Path $Job "ui-done.txt") -Value "done"
}
