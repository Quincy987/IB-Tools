<#
.SYNOPSIS
    Shared by the tests in tests\Release (dot-source it): unpack a release zip the way a user gets
    it, i.e. with every file marked as downloaded from the internet.
#>

# Windows puts this alternate data stream on downloads and on files unpacked from a downloaded zip.
function Set-IBToolsDownloaded([string]$Path) {
    Set-Content -LiteralPath $Path -Stream "Zone.Identifier" -Value "[ZoneTransfer]`r`nZoneId=3"
}

function Test-IBToolsBlocked([string]$Path) {
    return [bool](Get-Item -LiteralPath $Path -Stream "Zone.Identifier" -ErrorAction SilentlyContinue)
}

function Expand-IBToolsDownload([string]$Zip, [string]$Destination) {
    if (-not (Test-Path -LiteralPath $Zip)) { throw "Zip niet gevonden: $Zip (eerst .\scripts\release.ps1)" }
    Expand-Archive -LiteralPath $Zip -DestinationPath $Destination
    Get-ChildItem -LiteralPath $Destination -File | ForEach-Object { Set-IBToolsDownloaded $_.FullName }
    return $Destination
}

# For embedding a path in a single-quoted PowerShell string ('...'), e.g. for powershell -Command.
function ConvertTo-IBToolsQuoted([string]$Text) {
    return "'" + $Text.Replace("'", "''") + "'"
}
