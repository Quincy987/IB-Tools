<#
.SYNOPSIS
    UI half of the harness: fills in / confirms the "Modelpunt op coördinaat" window that the
    harness opened with PostCommand, reads the result window, writes ui-done.txt.
    Uses Windows UI Automation (WPF controls are found by their x:Name).
.EXAMPLE
    .\ui-test.ps1 -Job C:\...\job -RevitPid 12345
#>
param(
    [Parameter(Mandatory = $true)][string]$Job,
    [Parameter(Mandatory = $true)][int]$RevitPid,
    [int]$TimeoutSeconds = 600
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Drawing, System.Windows.Forms
$AE = [System.Windows.Automation.AutomationElement]
$Title = "Modelpunt op co" + [char]0x00F6 + "rdinaat"
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

try {
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while (-not (Test-Path (Join-Path $Job "ui-ready.txt"))) {
        if ((Get-Date) -gt $deadline) { throw "ui-ready.txt kwam niet" }
        Start-Sleep -Seconds 2
    }
    Say "Klembord verwacht: $(Get-Content (Join-Path $Job 'ui-expected.txt'))"

    $win = Find-Window "XBox" 120
    if (-not $win) { throw "Invulvenster niet gevonden" }
    Say ("Invulvenster: X='{0}' Y='{1}' hoogte='{2}' schaal='{3}'" -f (Get-Value $win "XBox"), (Get-Value $win "YBox"), (Get-Value $win "ZBox"), (Get-Value $win "TypeBox"))
    Say ("Info: " + (Get-Element $win "InfoText").Current.Name)
    Say ("Bron: " + (Get-Element $win "SourceText").Current.Name)
    Shot $win "ui-window.png"
    Click $win "OkButton"
    Say "Plaatsen geklikt"

    $res = Find-Window "DetailsBox" 120
    if (-not $res) { throw "Resultaatvenster niet gevonden" }
    Say ("Resultaat: " + (Get-Element $res "SummaryText").Current.Name)
    Say ("Details:`r`n" + (Get-Value $res "DetailsBox"))
    Shot $res "ui-result.png"
    Click $res "CloseButton"
    Say "Resultaatvenster gesloten"
}
catch {
    Say ("UI-TEST FOUT: " + $_.Exception.Message)
}
finally {
    Set-Content -Path (Join-Path $Job "ui-done.txt") -Value "done"
}
