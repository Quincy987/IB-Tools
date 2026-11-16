<#
.SYNOPSIS
    Shared by scripts\release.ps1 and tests\Release\*.ps1 (dot-source it): the names of the shipped
    files, the IB Tools version, and rendering Installeren.cmd + LEESMIJ.txt for one Revit year from
    the templates next to this file.
#>

# The shipped files keep their old names on purpose (README "Hernoemen"). The templates get them
# through {{DLL}} / {{ADDIN}}, so a rename changes them here and not in the templates.
$IBToolsDllName = "FoundationProjectionToggle.dll"
$IBToolsAddinName = "FoundationProjectionToggle.addin"
$IBToolsInstallFileNames = @("Installeren.cmd", "LEESMIJ.txt")
$script:IBToolsReleaseTemplates = $PSScriptRoot

function Get-IBToolsVersion([string]$Repo) {
    $csproj = Join-Path $Repo "src\IBTools\IBTools.csproj"
    $version = ([xml](Get-Content -Raw $csproj)).Project.PropertyGroup | ForEach-Object { $_.Version } |
        Where-Object { $_ } | Select-Object -First 1
    if (-not $version) { throw "Geen <Version> gevonden in $csproj" }
    return [string]$version
}

function New-IBToolsInstallFiles([string]$RevitVersion, [string]$Version, [string]$OutDir) {
    # Returns the paths of the rendered Installeren.cmd and LEESMIJ.txt (CRLF, plain ASCII).
    New-Item -ItemType Directory -Force $OutDir | Out-Null
    $out = @()
    foreach ($name in $IBToolsInstallFileNames) {
        $template = [IO.Path]::GetFileNameWithoutExtension($name) + ".template" + [IO.Path]::GetExtension($name)
        $text = [IO.File]::ReadAllText((Join-Path $script:IBToolsReleaseTemplates $template))
        $text = $text.Replace("{{REVIT}}", $RevitVersion).Replace("{{VERSION}}", $Version).
            Replace("{{DLL}}", $IBToolsDllName).Replace("{{ADDIN}}", $IBToolsAddinName)
        $text = ($text -replace "`r?`n", "`r`n")
        if ($text -match "\{\{") { throw "${template}: placeholder niet ingevuld." }
        # cmd and Notepad show anything else wrongly; control characters are typos (e.g. a "\v" escape).
        if ($text -match "[^\x20-\x7E\r\n\t]") { throw "${template}: bevat tekens buiten gewoon ASCII." }
        # The cmd header runs everything after the FIRST "##PS-START": it may occur only once.
        if ($name -eq "Installeren.cmd" -and ([regex]::Matches($text, "##PS-START")).Count -ne 1) {
            throw "${template}: de markering ##PS-START moet precies een keer voorkomen."
        }
        $path = Join-Path $OutDir $name
        [IO.File]::WriteAllText($path, $text, [Text.Encoding]::ASCII)
        $out += $path
    }
    return $out
}
