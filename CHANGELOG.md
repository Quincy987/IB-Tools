# Changelog

## [Unreleased]

### Toegevoegd
- Paalcoördinaten werkt automatisch bij, zonder op de knop te drukken (alleen in een model op
  coördinaten waarvan de zelftest op het projectbasispunt slaagt):
  - een paal die je verplaatst, kopieert of nieuw plaatst krijgt direct de juiste
    X-/Y-coördinaat_funderingspaal, in dezelfde stap (één Ctrl+Z);
  - wijzigt het coördinatenstelsel (projectbasispunt, meetpunt, projectlocatie), dan worden alle
    palen bijgewerkt;
  - bij het openen van een model vraagt IB Tools "Paalcoördinaten bijwerken?" als er palen zijn
    waarvan de coördinaten niet meer kloppen of leeg zijn: *Nu bijwerken* of *Niet nu*.
  De automatische update is optioneel geregistreerd: collega's zonder IB Tools krijgen geen
  melding. `IBTOOLS_PILECHECK=0` zet de vraag bij openen uit (`build.ps1` doet dat voor de Revit die
  het start). Revit-test: `tests/RevitHarness/Paalcoordinaten/run-paalcoordinaten.ps1`
  (47/47 in Revit 2023, 2/2 in een Revit zonder IB Tools; 2022, 2024 en 2025 alleen gecompileerd),
  verslag in `src/IBTools/Tools/Paalcoordinaten/verify/paalcoordinaten-automatisch-2026-11-17.md`.

## v2.1.0 — Installatiescript in elke release-zip (2026-11-16)

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
- `scripts/release.ps1`: tests en per Revit-versie (2022–2025) een release-zip; het publiceert
  niets.

### Gewijzigd
- `scripts/release.ps1` controleert dat elke zip precies de vier bestanden bevat. De namen van de
  geleverde bestanden en het invullen van de sjablonen staan in `scripts/release/ReleaseFiles.ps1`.
- De installatie-uitleg in de README gaat nu via `Installeren.cmd`, met een handmatige route
  (deblokkeren en kopiëren).
- De repo is ingedeeld als bibliotheek van tools: `src/IBTools/` met `Shared/` en een map per tool
  onder `Tools/<Tool>/` (code, iconen, families, README, testverslagen, demo). Er zijn algemene
  installatie-instructies, een README per tool en `docs/ONTWIKKELEN.md` voor het toevoegen van een
  tool. De namespaces zijn nu `IBTools.*`. De geleverde bestanden heten nog
  `FoundationProjectionToggle.dll/.addin`, met hetzelfde ClientId, zodat bijwerken alleen
  overschrijven is.
- Logs staan in `%LOCALAPPDATA%\IBTools\logs\` en worden aangezet met `IBTOOLS_DEBUG=1`. De oude
  `PROJECTION_TOGGLE_DEBUG=1` werkt nog.
- Revit-integratietests per tool in `tests/RevitHarness/<Tool>/`. Ze controleren het hele lint en
  laden elke meegeleverde familie.
