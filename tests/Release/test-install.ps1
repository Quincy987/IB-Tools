<#
.SYNOPSIS
    Tests Installeren.cmd from the release zips without touching Revit or the real Addins folder:
    every run gets its own APPDATA and a fake "Program Files" in %TEMP%. The files from the zip are
    marked as downloaded (Zone.Identifier, ZoneId=3), like Windows does when a downloaded zip is
    unpacked. Checks per Revit year: installed, unblocked, identical to the zip, no leftovers, a
    second run (update) works; for the net48 years also that .NET Framework refuses LoadFrom on the
    marked dll before and accepts the installed one. Once: Revit not installed (exit 3 / J goes on),
    dll in use (exit 4, nothing touched), a failure halfway (exit 5, previous install back), another
    IB Tools with the same ClientId in ProgramData or under another name (exit 6), zip not unpacked
    (exit 2). ProgramData is faked as well (with an unrelated add-in that must not get in the way).
.PARAMETER ZipDir
    Folder with IBTools-v<version>-Revit<year>.zip. Default: the output of scripts\release.ps1.
.EXAMPLE
    .\scripts\release.ps1; .\tests\Release\test-install.ps1
#>
param([string]$ZipDir)
$ErrorActionPreference = "Stop"
$repo = Resolve-Path (Join-Path $PSScriptRoot "..\..")
. (Join-Path $repo "scripts\release\ReleaseFiles.ps1")
. (Join-Path $PSScriptRoot "ReleaseTestHelpers.ps1")
$version = Get-IBToolsVersion $repo
if (-not $ZipDir) { $ZipDir = Join-Path $env:LOCALAPPDATA "IBTools\release\v$version" }
$years = @("2022", "2023", "2024", "2025")
$dllName = $IBToolsDllName
$addinName = $IBToolsAddinName
$work = Join-Path $env:TEMP ("ibtools-install-test-" + [guid]::NewGuid().ToString("N").Substring(0, 8))
New-Item -ItemType Directory -Force $work | Out-Null

$results = New-Object System.Collections.ArrayList
function Check([string]$name, [bool]$ok, [string]$detail = "") {
    $word = if ($ok) { "PASS" } else { "FAIL" }
    [void]$results.Add([pscustomobject]@{ Result = $word; Check = $name; Detail = $detail })
    Write-Host ("  {0}  {1}  {2}" -f $word, $name, $detail) -ForegroundColor $(if ($ok) { "Green" } else { "Red" })
}

function New-Manifest([string]$Dir, [string]$Name, [string]$ClientId) {
    New-Item -ItemType Directory -Force $Dir | Out-Null
    Set-Content -LiteralPath (Join-Path $Dir $Name) -Value ("<RevitAddIns><AddIn Type=""Application""><ClientId>$ClientId</ClientId>" +
        "<Assembly>other.dll</Assembly></AddIn></RevitAddIns>")
}
# Machine-wide add-ins (ProgramData) are faked too. The default one holds an unrelated add-in per
# year, which must not stop the installer.
$defaultProgramData = Join-Path $work "programdata"
foreach ($y in $years) { New-Manifest (Join-Path $defaultProgramData "Autodesk\Revit\Addins\$y") "Ander.addin" ([guid]::NewGuid()) }
$clientId = [regex]::Match((Get-Content -Raw (Join-Path $repo $IBToolsAddinName)), '<ClientId>\s*([^<]+?)\s*</ClientId>').Groups[1].Value

function Invoke-Installer([string]$Cmd, [string]$AppData, [string]$ProgramFiles, [string]$Assume, [string]$ProgramData = $defaultProgramData) {
    # Started the way Explorer starts a double-clicked .cmd: cmd.exe /c ""<path>"".
    $psi = New-Object System.Diagnostics.ProcessStartInfo "cmd.exe", ('/c ""{0}""' -f $Cmd)
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true; $psi.RedirectStandardError = $true; $psi.RedirectStandardInput = $true
    $psi.EnvironmentVariables["APPDATA"] = $AppData
    $psi.EnvironmentVariables["ProgramData"] = $ProgramData
    $psi.EnvironmentVariables["IBTOOLS_INSTALL_UNATTENDED"] = "1"
    $psi.EnvironmentVariables["IBTOOLS_INSTALL_PROGRAMFILES"] = $ProgramFiles
    if ($psi.EnvironmentVariables.ContainsKey("IBTOOLS_INSTALL_ASSUME")) { $psi.EnvironmentVariables.Remove("IBTOOLS_INSTALL_ASSUME") }
    if ($Assume) { $psi.EnvironmentVariables["IBTOOLS_INSTALL_ASSUME"] = $Assume }
    $p = [System.Diagnostics.Process]::Start($psi)
    $p.StandardInput.Close()                       # a question that slips through reads EOF instead of hanging
    $out = $p.StandardOutput.ReadToEndAsync(); $err = $p.StandardError.ReadToEndAsync()
    if (-not $p.WaitForExit(60000)) {
        & taskkill.exe /T /F /PID $p.Id 2>&1 | Out-Null   # cmd and the powershell it started
        return [pscustomobject]@{ Code = -1; Output = "TIMEOUT" }
    }
    $p.WaitForExit()
    return [pscustomobject]@{ Code = $p.ExitCode; Output = ($out.Result + $err.Result) }
}

function Start-PowerShell48([string]$Script) {
    # A fresh Windows PowerShell 5.1 = .NET Framework 4.8, the same loader rules as Revit 2022-2024.
    $psi = New-Object System.Diagnostics.ProcessStartInfo "powershell.exe",
        ("-NoProfile -EncodedCommand " + [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($Script)))
    $psi.UseShellExecute = $false; $psi.RedirectStandardOutput = $true
    # Its stderr (progress records as CLIXML) is swallowed, so it does not end up in our own output.
    $psi.RedirectStandardError = $true
    $p = [System.Diagnostics.Process]::Start($psi)
    [void]$p.StandardError.ReadToEndAsync()
    return $p
}
function Get-LoadScript([string]$Dll) {
    # Sets $r to "LOADED" or "REFUSED <exception>" and writes it to stdout at once.
    return "`$r = try { [void][Reflection.Assembly]::LoadFrom($(ConvertTo-IBToolsQuoted $Dll)); 'LOADED' } " +
        "catch { `$e = `$_.Exception; if (`$e.InnerException) { `$e = `$e.InnerException }; 'REFUSED ' + `$e.GetType().Name }; " +
        "[Console]::Out.WriteLine(`$r); [Console]::Out.Flush()"
}
function Test-LoadFrom([string]$Dll) {
    $p = Start-PowerShell48 (Get-LoadScript $Dll)
    $line = $p.StandardOutput.ReadToEnd().Trim()
    $p.WaitForExit()
    return $line
}
function Get-Leftovers([string]$Dir, [string]$Filter = "*.ibtools-*") {
    return @(Get-ChildItem -LiteralPath $Dir -Filter $Filter -ErrorAction SilentlyContinue | ForEach-Object { $_.Name })
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
    return Expand-IBToolsDownload (Join-Path $ZipDir "IBTools-v$version-Revit$Year.zip") (Join-Path $work $Name)
}

Write-Host "Installeren.cmd testen: v$version, zips uit $ZipDir" -ForegroundColor Cyan
try {
    foreach ($y in $years) {
        Write-Host "Revit ${y}:" -ForegroundColor Yellow
        # Spaces, brackets, an ampersand and a non-ASCII letter in the path, like
        # "Coordinaten\IBTools (1)" with a diaeresis (built with [char] to keep this file ASCII).
        $src = Expand-Download $y ("Co" + [char]0x00F6 + "rdinaten $y\IBTools (1) & co")
        $pf = New-FakeProgramFiles (Join-Path $work "pf-$y") @($y)
        $appData = Join-Path $work "appdata-$y"
        $target = Join-Path $appData "Autodesk\Revit\Addins\$y"
        $net48 = [int]$y -lt 2025
        $zipNames = @(Get-ChildItem -LiteralPath $src -File | ForEach-Object { $_.Name } | Sort-Object)
        $wantNames = @((@($dllName, $addinName) + $IBToolsInstallFileNames) | Sort-Object)
        Check "$y zip bevat precies de vier bestanden" (($zipNames -join ",") -eq ($wantNames -join ",")) ($zipNames -join ", ")
        Check "$y alle bestanden uit de zip gemarkeerd als download" (@(Get-ChildItem -LiteralPath $src -File | Where-Object { -not (Test-IBToolsBlocked $_.FullName) }).Count -eq 0) ""
        if ($net48) {
            $before = Test-LoadFrom (Join-Path $src $dllName)
            Check "$y gemarkeerde dll wordt geweigerd door .NET 4.8" ($before -like "REFUSED*") $before
        }
        $r = Invoke-Installer (Join-Path $src "Installeren.cmd") $appData $pf ""
        Check "$y installeren: exit 0" ($r.Code -eq 0) "exit $($r.Code)"
        if ($r.Code -ne 0) { Write-Host $r.Output }
        Check "$y uitvoer noemt Revit $y en de versie" (($r.Output -match "Revit $y gevonden") -and ($r.Output -match "Geinstalleerd: IB Tools dll-versie $([regex]::Escape($version))")) ""
        foreach ($f in @($dllName, $addinName)) {
            $t = Join-Path $target $f
            $ok = (Test-Path -LiteralPath $t) -and (-not (Test-IBToolsBlocked $t)) -and
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
    $pf23 = New-FakeProgramFiles (Join-Path $work "pf-2023") @("2023")

    # Revit 2023 not installed (only 2024): asks; no answer or N stops, J goes on.
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
    $appData = Join-Path $work "appdata-inuse"
    $target = Join-Path $appData "Autodesk\Revit\Addins\2023"
    $dll = Join-Path $target $dllName
    $r = Invoke-Installer $cmd $appData $pf23 ""
    Check "in gebruik: eerste installatie exit 0" ($r.Code -eq 0) "exit $($r.Code)"
    if ($r.Code -eq 0) {
        $stamp = (Get-Item -LiteralPath $dll).LastWriteTimeUtc
        $listing = (Get-ChildItem -LiteralPath $target | ForEach-Object { $_.Name }) -join ","
        $holder = Start-PowerShell48 ((Get-LoadScript $dll) + "; Start-Sleep -Seconds 120")
        try {
            $line = $holder.StandardOutput.ReadLine()
            Check "in gebruik: dll geladen door een .NET 4.8-testproces" ($line -eq "LOADED") $line
            $r = Invoke-Installer $cmd $appData $pf23 ""
            Check "in gebruik: exit 4 met 'Sluit Revit 2023'" (($r.Code -eq 4) -and ($r.Output -match "Sluit Revit 2023")) "exit $($r.Code)"
            $listingAfter = (Get-ChildItem -LiteralPath $target | ForEach-Object { $_.Name }) -join ","
            Check "in gebruik: niets aangeraakt" ((((Get-Item -LiteralPath $dll).LastWriteTimeUtc) -eq $stamp) -and ($listingAfter -eq $listing)) $listingAfter
        }
        finally {
            if (-not $holder.HasExited) { $holder.Kill() }     # our own test process, never Revit
            $holder.WaitForExit()
        }
    }

    # Failure halfway: the dll is already swapped when the manifest step fails (its .ibtools-old
    # slot is held open). Everything must be back as it was.
    $appData = Join-Path $work "appdata-rollback"
    $target = Join-Path $appData "Autodesk\Revit\Addins\2023"
    $r = Invoke-Installer $cmd $appData $pf23 ""
    Check "halverwege fout: eerste installatie exit 0" ($r.Code -eq 0) "exit $($r.Code)"
    if ($r.Code -eq 0) {
        $dll = Join-Path $target $dllName
        $addin = Join-Path $target $addinName
        Set-Content -LiteralPath $dll -Value "VORIGE-DLL"                  # recognisable "previous version"
        $addinHash = (Get-FileHash -LiteralPath $addin).Hash
        $slot = "$addin.ibtools-old"
        Set-Content -LiteralPath $slot -Value "bezet"
        $handle = [IO.File]::Open($slot, 'Open', 'Read', 'None')
        try {
            $r = Invoke-Installer $cmd $appData $pf23 ""
            Check "halverwege fout: exit 5 met 'niets veranderd'" (($r.Code -eq 5) -and ($r.Output -match "niets veranderd")) "exit $($r.Code)"
            $dllBack = (Get-Content -LiteralPath $dll -Raw).Trim() -eq "VORIGE-DLL"
            Check "halverwege fout: vorige dll en manifest terug" ($dllBack -and ((Get-FileHash -LiteralPath $addin).Hash -eq $addinHash)) ""
            $left = @(Get-Leftovers $target "*.ibtools-new") + @(Get-Leftovers $target "$dllName.ibtools-old")
            Check "halverwege fout: geen restbestanden" ($left.Count -eq 0) ($left -join ", ")
        }
        finally { $handle.Close() }
    }

    # Another IB Tools with the same ClientId: machine-wide (ProgramData) or under another name.
    $pdIT = Join-Path $work "programdata-it"
    New-Manifest (Join-Path $pdIT "Autodesk\Revit\Addins\2023") "IBTools-ICT.addin" $clientId
    $appData = Join-Path $work "appdata-machinewide"
    $target = Join-Path $appData "Autodesk\Revit\Addins\2023"
    $r = Invoke-Installer $cmd $appData $pf23 "" $pdIT
    Check "al in ProgramData: exit 6, noemt het bestand, niets gekopieerd" (($r.Code -eq 6) -and ($r.Output -match "IBTools-ICT\.addin") -and -not (Test-Path $target)) "exit $($r.Code)"
    $appData = Join-Path $work "appdata-othername"
    $target = Join-Path $appData "Autodesk\Revit\Addins\2023"
    New-Manifest $target "IBTools-oud.addin" $clientId.ToLowerInvariant()
    $r = Invoke-Installer $cmd $appData $pf23 ""
    Check "al onder een andere naam: exit 6, niets gekopieerd" (($r.Code -eq 6) -and ($r.Output -match "IBTools-oud\.addin") -and -not (Test-Path (Join-Path $target $dllName))) "exit $($r.Code)"

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
