@echo off
rem IB Tools {{VERSION}} voor Revit {{REVIT}} - installatie. Dubbelklik dit bestand.
rem Kopieert {{DLL}} en {{ADDIN}} naar %APPDATA%\Autodesk\Revit\Addins\{{REVIT}}\
rem en haalt de Windows-downloadblokkade eraf. Alles onder de PS-START-regel is PowerShell.
setlocal
set "IBT_SELF=%~f0"
"%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -ExecutionPolicy Bypass -Command "$s=[IO.File]::ReadAllText($env:IBT_SELF); iex $s.Substring($s.IndexOf('#'+'#PS-START'))"
set "RC=%ERRORLEVEL%"
if not "%IBTOOLS_INSTALL_UNATTENDED%"=="1" if not "%IBTOOLS_INSTALL_NOPAUSE%"=="1" pause
exit /b %RC%
##PS-START
# Exit codes: 0 geinstalleerd, 2 bestanden niet naast dit script (zip niet uitgepakt),
# 3 Revit {{REVIT}} niet gevonden en niet doorgegaan, 4 bestand in gebruik (Revit draait),
# 5 mislukt (gaat het omwisselen mis, dan wordt het teruggedraaid; de melding zegt of dat lukte),
# 6 IB Tools staat al op een andere plek (zelfde ClientId, bv. in ProgramData): niets veranderd.
# Test-instellingen (omgevingsvariabelen): IBTOOLS_INSTALL_UNATTENDED=1 (geen vragen, geen pauze),
# IBTOOLS_INSTALL_ASSUME=J (antwoord op de vraag), IBTOOLS_INSTALL_PROGRAMFILES (waar Revit staat).
$ErrorActionPreference = 'Stop'
$revit = '{{REVIT}}'
$version = '{{VERSION}}'
$files = @('{{DLL}}', '{{ADDIN}}')   # de dll eerst: die wordt ook als eerste omgewisseld
$unattended = ($env:IBTOOLS_INSTALL_UNATTENDED -eq '1')

function Say([string]$text, [string]$color = 'Gray') { Write-Host $text -ForegroundColor $color }
function Stop-Install([int]$code, [string]$text) {
    Write-Host ''
    Write-Host $text -ForegroundColor Red
    exit $code
}

Say ''
Say "IB Tools $version voor Revit $revit - installeren" 'Cyan'
Say ''

try {
    # 1. De bestanden moeten naast dit script staan (niet vanuit de zip starten).
    $src = Split-Path -Parent $env:IBT_SELF
    foreach ($f in $files) {
        if (-not (Test-Path -LiteralPath (Join-Path $src $f))) {
            Stop-Install 2 ("Niet gevonden naast dit script: $f`r`n" +
                "Pak eerst de zip uit (rechtsklik > Alles uitpakken) en dubbelklik dan Installeren.cmd in de uitgepakte map.")
        }
    }

    # 2. Is deze Revit-versie geinstalleerd?
    $pf = $env:IBTOOLS_INSTALL_PROGRAMFILES
    if (-not $pf) { $pf = $env:ProgramW6432 }
    if (-not $pf) { $pf = $env:ProgramFiles }
    $revitExe = Join-Path $pf "Autodesk\Revit $revit\Revit.exe"
    if (Test-Path -LiteralPath $revitExe) {
        Say "Revit $revit gevonden." 'Green'
    } else {
        $found = @(Get-ChildItem -LiteralPath (Join-Path $pf 'Autodesk') -Directory -Filter 'Revit 20*' -ErrorAction SilentlyContinue |
            Where-Object { Test-Path -LiteralPath (Join-Path $_.FullName 'Revit.exe') } | ForEach-Object { $_.Name })
        Say "Revit $revit is niet gevonden op deze pc ($revitExe)." 'Yellow'
        if ($found.Count -gt 0) {
            Say ("Wel gevonden: {0}. Download dan de zip voor die versie." -f ($found -join ', ')) 'Yellow'
        }
        if ($unattended) { $answer = $env:IBTOOLS_INSTALL_ASSUME } else { $answer = Read-Host "Toch installeren voor Revit $revit? (J/N)" }
        if ("$answer".Trim().ToUpperInvariant() -notin @('J', 'JA', 'Y', 'YES')) {
            Stop-Install 3 'Niet geinstalleerd.'
        }
    }

    # 3. Doelmap van Revit (per gebruiker).
    $target = Join-Path $env:APPDATA "Autodesk\Revit\Addins\$revit"
    Say "Doelmap: $target"

    # 3b. Geen tweede IB Tools naast deze: een ander manifest met hetzelfde add-in-id, voor alle
    #     gebruikers (ProgramData, meestal door ICT) of onder een andere naam in de eigen map.
    $addinText = [IO.File]::ReadAllText((Join-Path $src $files[1]))
    $clientId = [regex]::Match($addinText, '<ClientId>\s*([^<]+?)\s*</ClientId>').Groups[1].Value
    if (-not $clientId) { Stop-Install 5 "Geen ClientId gevonden in $($files[1])." }
    $machine = Join-Path $env:ProgramData "Autodesk\Revit\Addins\$revit"
    $others = @(@($machine, $target) | Where-Object { Test-Path -LiteralPath $_ } |
        ForEach-Object { Get-ChildItem -LiteralPath $_ -Filter '*.addin' -File } |
        Where-Object { $_.FullName -ne (Join-Path $target $files[1]) } |
        Where-Object { [IO.File]::ReadAllText($_.FullName) -match [regex]::Escape($clientId) } |
        ForEach-Object { $_.FullName })
    if ($others.Count -gt 0) {
        Stop-Install 6 ("IB Tools staat hier al op een andere plek: " + ($others -join ', ') + "`r`n" +
            "Twee installaties naast elkaar geven problemen in Revit; er is niets veranderd.`r`n" +
            "Staat hij in ProgramData, vraag dan ICT om hem bij te werken of te verwijderen. " +
            "Staat hij in je eigen Addins-map, verwijder dan dat .addin-bestand en de dll waar het naar wijst.")
    }
    if (-not (Test-Path -LiteralPath $target)) { New-Item -ItemType Directory -Force -Path $target | Out-Null }

    # 4. Een draaiende Revit houdt de oude dll vast: dan niets aanraken.
    $existingDll = Join-Path $target $files[0]
    if (Test-Path -LiteralPath $existingDll) {
        try {
            $fs = [IO.File]::Open($existingDll, [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
            $fs.Close()
        } catch {
            Stop-Install 4 "De vorige versie is in gebruik. Sluit Revit $revit en dubbelklik Installeren.cmd opnieuw."
        }
    }

    # 5. Eerst naast de oude bestanden kopieren en deblokkeren, dan pas omwisselen. Gaat er
    #    onderweg iets mis, dan komt alles terug zoals het was: alleen wat echt verplaatst is
    #    ($moved) of neergezet ($placed) wordt teruggedraaid.
    $moved = @{}
    $placed = @{}
    try {
        foreach ($f in $files) {
            $new = Join-Path $target "$f.ibtools-new"
            Copy-Item -LiteralPath (Join-Path $src $f) -Destination $new -Force
            Unblock-File -LiteralPath $new
        }
        foreach ($f in $files) {
            $final = Join-Path $target $f
            $old = "$final.ibtools-old"
            if (Test-Path -LiteralPath $old) { Remove-Item -LiteralPath $old -Force }
            if (Test-Path -LiteralPath $final) { Move-Item -LiteralPath $final -Destination $old; $moved[$f] = $true }
            Move-Item -LiteralPath "$final.ibtools-new" -Destination $final; $placed[$f] = $true
        }
    } catch {
        $why = $_.Exception.Message
        foreach ($f in $files) {
            $final = Join-Path $target $f
            if ($placed[$f]) { Remove-Item -LiteralPath $final -Force -ErrorAction SilentlyContinue }
            if ($moved[$f]) { Move-Item -LiteralPath "$final.ibtools-old" -Destination $final -Force -ErrorAction SilentlyContinue }
            Remove-Item -LiteralPath "$final.ibtools-new" -Force -ErrorAction SilentlyContinue
        }
        # Only claim "nothing changed" when every file that was moved away is really back.
        $notBack = @($files | Where-Object { $moved[$_] -and -not (Test-Path -LiteralPath (Join-Path $target $_)) })
        if ($notBack.Count -eq 0) {
            Stop-Install 5 "Installeren mislukt: $why`r`nEr is niets veranderd: de vorige installatie staat er nog."
        }
        Stop-Install 5 ("Installeren mislukt: $why`r`nTerugzetten is niet helemaal gelukt. De vorige versie van " +
            ($notBack -join ', ') + " staat als .ibtools-old in $target; haal daar .ibtools-old van de naam af.")
    }
    foreach ($f in $files) {
        Remove-Item -LiteralPath (Join-Path $target "$f.ibtools-old") -Force -ErrorAction SilentlyContinue
    }

    # 6. Controle.
    foreach ($f in $files) {
        $p = Join-Path $target $f
        if (-not (Test-Path -LiteralPath $p)) { Stop-Install 5 "Controle mislukt: $p ontbreekt." }
        if (Get-Item -LiteralPath $p -Stream 'Zone.Identifier' -ErrorAction SilentlyContinue) {
            Stop-Install 5 "Controle mislukt: $p is nog geblokkeerd door Windows."
        }
    }
    $fileVersion = (Get-Item -LiteralPath $existingDll).VersionInfo.FileVersion
    Say "Geinstalleerd: IB Tools dll-versie $fileVersion, gedeblokkeerd." 'Green'
    if (Get-Process -Name 'Revit' -ErrorAction SilentlyContinue) {
        Say 'Let op: Revit draait nu. Het tabblad IB Tools verschijnt pas na opnieuw starten.' 'Yellow'
    }
    Say ''
    Say "Klaar. Start Revit $revit en klik 'Always Load' bij de vraag over IB Tools." 'Cyan'
    Say 'Het tabblad IB Tools staat dan in het lint.' 'Cyan'
    Say ''
    exit 0
} catch {
    Stop-Install 5 ("Installeren mislukt: " + $_.Exception.Message)
}
