# Changelog

## [Unreleased]

### Toegevoegd
- Elke release-zip bevat nu `Installeren.cmd` en `LEESMIJ.txt` naast de dll en het manifest.
  Dubbelklikken op `Installeren.cmd` kopieert IB Tools naar
  `%APPDATA%\Autodesk\Revit\Addins\<jaar>\` en haalt de Windows-downloadblokkade eraf. Zonder dat
  deblokkeren meldt Revit 2023 "External Tool Failure".
- Het installatiescript stopt zonder iets te veranderen als Revit de dll nog gebruikt (exit 4), als
  IB Tools al op een andere plek staat, bijvoorbeeld in ProgramData (exit 6), of als de zip niet
  is uitgepakt (exit 2). Gaat het omwisselen halverwege mis, dan zet het de vorige versie terug
  (exit 5).
- Tests voor het installatiescript: `tests/Release/test-install.ps1` (zonder Revit, 53 checks) en
  `tests/Release/revit-check-install.ps1` (in een echte Revit 2023, met het testslot).

### Gewijzigd
- `scripts/release.ps1` controleert dat elke zip precies de vier bestanden bevat. De namen van de
  geleverde bestanden en het invullen van de sjablonen staan in `scripts/release/ReleaseFiles.ps1`.
- De installatie-uitleg in de README gaat nu via `Installeren.cmd`, met een handmatige route
  (deblokkeren en kopiëren).
