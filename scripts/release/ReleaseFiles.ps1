<#
.SYNOPSIS
    Shared by scripts\release.ps1 and tests\Release\test-install.ps1: reads the IB Tools version
    and renders Installeren.cmd + LEESMIJ.txt for one Revit year from the templates next to this file.
#>

$script:IBToolsReleaseTemplates = $PSScriptRoot

function Get-IBToolsVersion([string]$Repo) {
    $csproj = Join-Path $Repo "src\IBTools\IBTools.csproj"
    $version = ([xml](Get-Content -Raw $csproj)).Project.PropertyGroup | ForEach-Object { $_.Version } |
        Where-Object { $_ } | Select-Object -First 1
    if (-not $version) { throw "Geen <Version> gevonden in $csproj" }
    return [string]$version
}

function New-IBToolsInstallFiles([string]$RevitVersion, [string]$Version, [string]$OutDir) {
    # Returns the paths of the rendered Installeren.cmd and LEESMIJ.txt (CRLF, ASCII only).
    New-Item -ItemType Directory -Force $OutDir | Out-Null
    $out = @()
    foreach ($pair in @(@("Installeren.template.cmd", "Installeren.cmd"), @("LEESMIJ.template.txt", "LEESMIJ.txt"))) {
        $text = [IO.File]::ReadAllText((Join-Path $script:IBToolsReleaseTemplates $pair[0]))
        $text = $text.Replace("{{REVIT}}", $RevitVersion).Replace("{{VERSION}}", $Version)
        $text = ($text -replace "`r?`n", "`r`n")
        if ($text -match "\{\{") { throw "$($pair[0]): placeholder niet ingevuld." }
        # The cmd header runs everything after the FIRST "##PS-START": it may occur only once.
        if ($pair[1] -eq "Installeren.cmd" -and ([regex]::Matches($text, "##PS-START")).Count -ne 1) {
            throw "$($pair[0]): de markering ##PS-START moet precies een keer voorkomen."
        }
        if ($text -match "[^\x00-\x7F]") { throw "$($pair[0]): bevat niet-ASCII-tekens (cmd en Kladblok tonen die verkeerd)." }
        $path = Join-Path $OutDir $pair[1]
        [IO.File]::WriteAllText($path, $text, [Text.Encoding]::ASCII)
        $out += $path
    }
    return $out
}
