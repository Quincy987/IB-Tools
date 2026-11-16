<#
.SYNOPSIS
    Builds the release zips of IB Tools: runs the unit tests, builds Release for Revit 2022, 2023,
    2024 and 2025 (compile only, never touches Revit) and packs per Revit version
    IBTools-v<version>-Revit<year>.zip with FoundationProjectionToggle.dll + .addin.
    Publishes NOTHING: create the GitHub release yourself afterwards.
.PARAMETER OutDir
    Where the zips go. Default: %LOCALAPPDATA%\IBTools\release\v<version>\ (the version comes from
    src\IBTools\IBTools.csproj).
.EXAMPLE
    .\scripts\release.ps1
    gh release create v2.1.0 -R Quincy987/IB-Tools --target development --title "v2.1.0 - IB Tools" `
        --notes-file notes.md "$env:LOCALAPPDATA\IBTools\release\v2.1.0\*.zip"
#>
param(
    [string]$OutDir
)
$ErrorActionPreference = "Stop"
$repo = Resolve-Path (Join-Path $PSScriptRoot "..")
$csproj = Join-Path $repo "src\IBTools\IBTools.csproj"
$manifest = Join-Path $repo "FoundationProjectionToggle.addin"
$dllName = "FoundationProjectionToggle.dll"
$versions = @("2022", "2023", "2024", "2025")

$version = ([xml](Get-Content -Raw $csproj)).Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ } | Select-Object -First 1
if (-not $version) { throw "Geen <Version> gevonden in $csproj" }
if (-not $OutDir) { $OutDir = Join-Path $env:LOCALAPPDATA "IBTools\release\v$version" }

Write-Host "IB Tools release v$version -> $OutDir" -ForegroundColor Cyan
Push-Location $repo
try {
    Write-Host "[1/3] Unit tests..." -ForegroundColor Yellow
    & dotnet test --nologo -v q
    if ($LASTEXITCODE -ne 0) { throw "Unit tests mislukt (exit $LASTEXITCODE); geen release gemaakt." }

    if (Test-Path $OutDir) { Remove-Item -Recurse -Force $OutDir }
    New-Item -ItemType Directory -Force $OutDir | Out-Null

    Write-Host "[2/3] Builds per Revit-versie..." -ForegroundColor Yellow
    foreach ($v in $versions) {
        & (Join-Path $repo "build.ps1") -NoDeploy -RevitVersion $v | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "Build voor Revit $v mislukt (exit $LASTEXITCODE)." }
        $dll = Join-Path $repo "src\IBTools\bin\Release\Revit$v\$dllName"
        if (-not (Test-Path $dll)) { throw "Niet gevonden na de build: $dll" }

        # Sanity: right version and right framework (2025 = .NET 8, the rest = .NET Framework 4.8).
        $fileVersion = (Get-Item $dll).VersionInfo.FileVersion
        if (-not $fileVersion.StartsWith($version)) { throw "Revit ${v}: dll-versie $fileVersion is niet $version." }
        $text = [System.Text.Encoding]::ASCII.GetString([System.IO.File]::ReadAllBytes($dll))
        $expected = if ([int]$v -ge 2025) { ".NETCoreApp,Version=v8.0" } else { ".NETFramework,Version=v4.8" }
        if (-not $text.Contains($expected)) { throw "Revit ${v}: dll is niet gebouwd voor $expected." }

        $zip = Join-Path $OutDir "IBTools-v$version-Revit$v.zip"
        Compress-Archive -Path $dll, $manifest -DestinationPath $zip
        Write-Host ("    Revit {0}: {1} ({2}, dll {3})" -f $v, (Split-Path $zip -Leaf), $expected, $fileVersion) -ForegroundColor Green
    }

    Write-Host "[3/3] Klaar. Niets gepubliceerd." -ForegroundColor Yellow
    Get-ChildItem $OutDir -Filter *.zip | Select-Object Name, @{ n = "KB"; e = { [int]($_.Length / 1KB) } } | Format-Table -AutoSize | Out-Host
}
finally {
    Pop-Location
}
