<#
.SYNOPSIS
    Tests Installeren.cmd from the release zips without touching Revit or the real Addins folder:
    every run gets its own APPDATA and a fake "Program Files" in %TEMP%. The files from the zip are
    marked as downloaded (Zone.Identifier, ZoneId=3), like Windows does when a downloaded zip is
    unpacked. Checks per Revit year: installed, unblocked, identical to the zip, no leftovers, a
    second run (update) works; for the net48 years also that .NET Framework refuses LoadFrom on the
    marked dll before and accepts the installed one. Once: Revit not installed (exit 3 / J goes on),
    dll in use (exit 4, nothing touched), zip not unpacked (exit 2).
.PARAMETER ZipDir
    Folder with IBTools-v<version>-Revit<year>.zip. Default: the output of scripts\release.ps1.
.EXAMPLE
    .\scripts\release.ps1; .\tests\Release\test-install.ps1
#>
param([string]$ZipDir)
$ErrorActionPreference = "Stop"
$repo = Resolve-Path (Join-Path $PSScriptRoot "..\..")
. (Join-Path $repo "scripts\release\ReleaseFiles.ps1")
$version = Get-IBToolsVersion $repo
if (-not $ZipDir) { $ZipDir = Join-Path $env:LOCALAPPDATA "IBTools\release\v$version" }
$years = @("2022", "2023", "2024", "2025")
$dllName = "FoundationProjectionToggle.dll"
$addinName = "FoundationProjectionToggle.addin"
$work = Join-Path $env:TEMP ("ibtools-install-test-" + [guid]::NewGuid().ToString("N").Substring(0, 8))
New-Item -ItemType Directory -Force $work | Out-Null

$results = New-Object System.Collections.ArrayList
function Check([string]$name, [bool]$ok, [string]$detail = "") {
    $word = if ($ok) { "PASS" } else { "FAIL" }
    [void]$results.Add([pscustomobject]@{ Result = $word; Check = $name; Detail = $detail })
    Write-Host ("  {0}  {1}  {2}" -f $word, $name, $detail) -ForegroundColor $(if ($ok) { "Green" } else { "Red" })
}

function Invoke-Installer([string]$Cmd, [string]$AppData, [string]$ProgramFiles, [string]$Assume) {
    # Started the way Explorer starts a double-clicked .cmd: cmd.exe /c ""<path>"".
    $psi = New-Object System.Diagnostics.ProcessStartInfo "cmd.exe", ('/c ""{0}""' -f $Cmd)
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true; $psi.RedirectStandardError = $true; $psi.RedirectStandardInput = $true
    $psi.EnvironmentVariables["APPDATA"] = $AppData
    $psi.EnvironmentVariables["IBTOOLS_INSTALL_UNATTENDED"] = "1"
    $psi.EnvironmentVariables["IBTOOLS_INSTALL_PROGRAMFILES"] = $ProgramFiles
    if ($psi.EnvironmentVariables.ContainsKey("IBTOOLS_INSTALL_ASSUME")) { $psi.EnvironmentVariables.Remove("IBTOOLS_INSTALL_ASSUME") }
    if ($Assume) { $psi.EnvironmentVariables["IBTOOLS_INSTALL_ASSUME"] = $Assume }
    $p = [System.Diagnostics.Process]::Start($psi)
    $p.StandardInput.Close()                       # a question that slips through reads EOF instead of hanging
    $out = $p.StandardOutput.ReadToEndAsync(); $err = $p.StandardError.ReadToEndAsync()
    if (-not $p.WaitForExit(60000)) { $p.Kill(); return [pscustomobject]@{ Code = -1; Output = "TIMEOUT" } }
    $p.WaitForExit()
    return [pscustomobject]@{ Code = $p.ExitCode; Output = ($out.Result + $err.Result) }
}

function Set-Downloaded([string]$Path) {
    Set-Content -LiteralPath $Path -Stream "Zone.Identifier" -Value "[ZoneTransfer]`r`nZoneId=3"
}
function Test-Blocked([string]$Path) {
    return [bool](Get-Item -LiteralPath $Path -Stream "Zone.Identifier" -ErrorAction SilentlyContinue)
}
function Test-LoadFrom([string]$Dll) {
    # A fresh Windows PowerShell 5.1 = .NET Framework 4.8, the same loader rules as Revit 2022-2024.
    $code = "try { [void][Reflection.Assembly]::LoadFrom('$Dll'); 'LOADED' } catch { 'REFUSED ' + `$_.Exception.GetType().Name }"
    return (& powershell.exe -NoProfile -Command $code | Out-String).Trim()
}
function Get-Leftovers([string]$Dir) {
    return @(Get-ChildItem -LiteralPath $Dir -Filter "*.ibtools-*" -ErrorAction SilentlyContinue | ForEach-Object { $_.Name })
}
function New-FakeProgramFiles([string]$Dir, [string[]]$Revits) {
    New-Item -ItemType Directory -Force $Dir | Out-Null
    foreach ($r in $Revits) {
        $d = Join-Path $Dir "Autodesk\Revit $r"
        New-Item -ItemType Directory -Force $d | Out-Null
        Set-Content -LiteralPath (Join-Path $d "Revit.exe") -Value "fake"
    }
    return $Dir
}
function Expand-Download([string]$Year, [string]$Name) {
    # Unpack like a user would, then mark every file as downloaded.
    $zip = Join-Path $ZipDir "IBTools-v$version-Revit$Year.zip"
    if (-not (Test-Path $zip)) { throw "Zip niet gevonden: $zip (eerst .\scripts\release.ps1)" }
    $dir = Join-Path $work $Name
    Expand-Archive -LiteralPath $zip -DestinationPath $dir
    Get-ChildItem -LiteralPath $dir -File | ForEach-Object { Set-Downloaded $_.FullName }
    return $dir
}

Write-Host "Installeren.cmd testen: v$version, zips uit $ZipDir" -ForegroundColor Cyan
try {
    foreach ($y in $years) {
        Write-Host "Revit ${y}:" -ForegroundColor Yellow
        # Spaces, brackets and an ampersand in the path, like "Downloads\IBTools (1)".
        $src = Expand-Download $y "Mijn downloads $y\IBTools (1) & co"
        $pf = New-FakeProgramFiles (Join-Path $work "pf-$y") @($y)
        $appData = Join-Path $work "appdata-$y"
        $target = Join-Path $appData "Autodesk\Revit\Addins\$y"
        $net48 = [int]$y -lt 2025
        Check "$y alle bestanden uit de zip gemarkeerd als download" (@(Get-ChildItem -LiteralPath $src -File | Where-Object { -not (Test-Blocked $_.FullName) }).Count -eq 0) ""
        if ($net48) {
            $before = Test-LoadFrom (Join-Path $src $dllName)
            Check "$y gemarkeerde dll wordt geweigerd door .NET 4.8" ($before -like "REFUSED*") $before
        }
        $r = Invoke-Installer (Join-Path $src "Installeren.cmd") $appData $pf ""
        Check "$y installeren: exit 0" ($r.Code -eq 0) "exit $($r.Code)"
        if ($r.Code -ne 0) { Write-Host $r.Output }
        Check "$y uitvoer noemt Revit $y en 'Geinstalleerd'" (($r.Output -match "Revit $y gevonden") -and ($r.Output -match "Geinstalleerd: IB Tools dll-versie $([regex]::Escape($version))")) ""
        foreach ($f in @($dllName, $addinName)) {
            $t = Join-Path $target $f
            $ok = (Test-Path -LiteralPath $t) -and (-not (Test-Blocked $t)) -and
                ((Get-FileHash -LiteralPath $t).Hash -eq (Get-FileHash -LiteralPath (Join-Path $src $f)).Hash)
            Check "$y $f staat er, gedeblokkeerd, gelijk aan de zip" $ok ""
        }
        Check "$y geen restbestanden" ((Get-Leftovers $target).Count -eq 0) ((Get-Leftovers $target) -join ", ")
        if ($net48) {
            $after = Test-LoadFrom (Join-Path $target $dllName)
            Check "$y geinstalleerde dll laadt in .NET 4.8" ($after -eq "LOADED") $after
        }
        $r2 = Invoke-Installer (Join-Path $src "Installeren.cmd") $appData $pf ""
        Check "$y tweede keer (bijwerken): exit 0, geen restbestanden" (($r2.Code -eq 0) -and ((Get-Leftovers $target).Count -eq 0)) "exit $($r2.Code)"
    }

    Write-Host "Bijzondere gevallen (zip van Revit 2023):" -ForegroundColor Yellow
    $src = Expand-Download "2023" "special"
    $cmd = Join-Path $src "Installeren.cmd"

    $pf24 = New-FakeProgramFiles (Join-Path $work "pf-only2024") @("2024")
    $appData = Join-Path $work "appdata-norevit"
    $dll = Join-Path $appData "Autodesk\Revit\Addins\2023\$dllName"
    $r = Invoke-Installer $cmd $appData $pf24 ""
    Check "Revit 2023 ontbreekt, geen antwoord: exit 3, niets gekopieerd" (($r.Code -eq 3) -and -not (Test-Path $dll)) "exit $($r.Code)"
    Check "Revit 2023 ontbreekt: noemt de gevonden Revit 2024" ($r.Output -match "Wel gevonden: Revit 2024") ""
    $r = Invoke-Installer $cmd $appData $pf24 "N"
    Check "Revit 2023 ontbreekt, antwoord N: exit 3, niets gekopieerd" (($r.Code -eq 3) -and -not (Test-Path $dll)) "exit $($r.Code)"
    $r = Invoke-Installer $cmd $appData $pf24 "J"
    Check "Revit 2023 ontbreekt, antwoord J: exit 0, geinstalleerd" (($r.Code -eq 0) -and (Test-Path $dll)) "exit $($r.Code)"

    # In use: a .NET Framework process holds the installed dll loaded, like a running Revit does.
    $pf23 = New-FakeProgramFiles (Join-Path $work "pf-2023") @("2023")
    $appData = Join-Path $work "appdata-inuse"
    $target = Join-Path $appData "Autodesk\Revit\Addins\2023"
    $dll = Join-Path $target $dllName
    $r = Invoke-Installer $cmd $appData $pf23 ""
    Check "in-gebruik-test: eerste installatie exit 0" ($r.Code -eq 0) "exit $($r.Code)"
    $stamp = (Get-Item -LiteralPath $dll).LastWriteTimeUtc
    $listing = (Get-ChildItem -LiteralPath $target | ForEach-Object { $_.Name }) -join ","
    $holderScript = "[void][Reflection.Assembly]::LoadFrom('$dll'); [Console]::Out.WriteLine('LOADED'); [Console]::Out.Flush(); Start-Sleep -Seconds 120"
    $hpsi = New-Object System.Diagnostics.ProcessStartInfo "powershell.exe", ('-NoProfile -Command "{0}"' -f $holderScript)
    $hpsi.UseShellExecute = $false; $hpsi.RedirectStandardOutput = $true
    $holder = [System.Diagnostics.Process]::Start($hpsi)
    try {
        $line = $holder.StandardOutput.ReadLine()
        Check "dll geladen door een .NET 4.8-testproces" ($line -eq "LOADED") $line
        $r = Invoke-Installer $cmd $appData $pf23 ""
        Check "dll in gebruik: exit 4 met 'Sluit Revit 2023'" (($r.Code -eq 4) -and ($r.Output -match "Sluit Revit 2023")) "exit $($r.Code)"
        $listingAfter = (Get-ChildItem -LiteralPath $target | ForEach-Object { $_.Name }) -join ","
        Check "dll in gebruik: niets aangeraakt" ((((Get-Item -LiteralPath $dll).LastWriteTimeUtc) -eq $stamp) -and ($listingAfter -eq $listing)) $listingAfter
    }
    finally {
        if (-not $holder.HasExited) { $holder.Kill() }     # our own test process, never Revit
        $holder.WaitForExit()
    }

    # Zip not unpacked: Explorer extracts only the clicked .cmd to a temp folder.
    $lonely = Join-Path $work "alleen-cmd"
    New-Item -ItemType Directory -Force $lonely | Out-Null
    Copy-Item -LiteralPath $cmd -Destination $lonely
    $r = Invoke-Installer (Join-Path $lonely "Installeren.cmd") (Join-Path $work "appdata-lonely") $pf23 ""
    Check "zip niet uitgepakt: exit 2 met 'Pak eerst de zip uit'" (($r.Code -eq 2) -and ($r.Output -match "Pak eerst de zip uit")) "exit $($r.Code)"
}
finally {
    Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
}

$failed = @($results | Where-Object { $_.Result -eq "FAIL" })
Write-Host ""
Write-Host ("Installeren.cmd: {0}/{1} checks geslaagd" -f ($results.Count - $failed.Count), $results.Count) -ForegroundColor $(if ($failed.Count) { "Red" } else { "Green" })
if ($failed.Count) { exit 1 }
exit 0
