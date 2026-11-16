# Verificatie herindeling "IB Tools als bibliotheek" — 2026-11-16

Branch `claude-bot/t-582` vanaf `development` (930cd55, v2.0.0). Doel: de repo per tool indelen
zonder het gedrag te veranderen.

## Wat is verplaatst

- `src/FoundationProjectionToggle/<laag>/` (Commands, Services, UI, Core, …) → `src/IBTools/`:
  `App.cs`, `Shared/` (wat meerdere tools gebruiken) en `Tools/<Tool>/` voor Projectielijnen,
  Worksets, Modelpunt, Paalcoordinaten en Remmingwerk, elk met eigen `Resources/icons` en
  `Resources/families`. 63 bestanden als `git mv` (historie behouden). Namespaces `IBTools`,
  `IBTools.Shared`, `IBTools.Tools.<Tool>(.Core)`.
- `RibbonManager` gesplitst: gedeelde helper voor tab/paneel/knop vs. de Hide/Show-status
  (`ProjectionToggleButton`).
- Twee unit-testprojecten → `tests/IBTools.Tests` met een map per tool (LangVersion 7.3 als
  bewaking), Revit-harnas per tool in `tests/RevitHarness/<Tool>/`.
- Testverslagen en de Remmingwerk-video naar `Tools/<Tool>/verify` en `Tools/Remmingwerk/demo`
  (buiten de build gehouden); `ibtools-demo.mp4` blijft in `docs/demo`.
- Logs naar `%LOCALAPPDATA%\IBTools\logs`, aan met `IBTOOLS_DEBUG=1` (oude variabele werkt nog).
- README per tool, nieuwe hoofd-README, `docs/ONTWIKKELEN.md`, `scripts/release.ps1`, versie 2.1.0.

**Bewust niet veranderd:** de geleverde bestanden `FoundationProjectionToggle.dll`/`.addin` en het
ClientId (alleen `FullClassName` in het manifest werd `IBTools.App`); tab, panelen, interne
knopnamen, labels en volgorde.

## Resources vóór en na

Vergeleken met `GetManifestResourceNames()` van de Release-dll voor Revit 2023:

| | vóór (v2.0.0) | na |
|---|---|---|
| iconen | 12 (`FoundationProjectionToggle.UI.Resources.icons.*`) | 12 (`IBTools.icons.*`) |
| families | 3 (`FoundationProjectionToggle.Resources.families.*`) | 3 (`IBTools.families.*`) |
| bestandsnamen | | identiek |

De naam hangt nu alleen van de bestandsnaam af, dus verplaatsen breekt niets meer.
`EmbeddedResourceTests` controleert dat elk gevraagd icoon en elke familie precies één keer bestaat.

## Gevonden en opgelost: oude DevTools-add-in

In de gedeelde Addins-map van Revit 2023 op de ontwikkel-pc staat `FoundationProjectionToggle.DevTools.dll`
(februari 2026, niet in deze repo). Uit zijn metadata: hij roept `Logger.Info/Warn/Error` aan onder
de oude namespace `FoundationProjectionToggle.Infrastructure`. Een dun, als obsolete gemarkeerd
doorgeefluik (`Shared/LegacyLogger.cs`) houdt hem werkend; in het Revit-journal start hij na de
herindeling met `API_SUCCESS` (de versiewaarschuwing 1.0.0.0 tegen 2.x bestond al met v2.0.0).

## Uitkomst

- `dotnet build` (hele oplossing): 0 waarschuwingen. `dotnet test`: **115 geslaagd** (109 bestaand +
  6 nieuwe resource-tests).
- `.\build.ps1 -NoDeploy` voor Revit 2022, 2023, 2024 en 2025: 0 waarschuwingen.
  `.\scripts\release.ps1`: vier zips, dll-versie 2.1.0.0, juiste framework per versie, manifest met
  `IBTools.App` en het oude ClientId.
- Revit 2023, `tests\RevitHarness\Remmingwerk\run-remmingwerk.ps1` (onbemand, eigen testslot):
  **61 van 61 controles OK**. Dat zijn de 55 Remmingwerk-controles van eerder (rechte lijnen, boog,
  kant links, ontbrekende staaltypen, echte knop + Undo) plus 6 nieuwe lintcontroles. Gedeployde dll
  2.1.0.0; tab IB Tools met panelen Weergave, Coördinaten, Model, Kademuren en knoppen
  ToggleProjection, PileCoordinates, CoordinatePoint, IbWorksets, Remmingwerk in die volgorde, elk
  met groot en klein icoon. Daarna eigen Revit gesloten, slot vrijgegeven, harnas uit de Addins-map.
- Het Revit-journal toont dat elke knop naar zijn klasse in `IBTools.Tools.<Tool>` wijst.

De bestaande verslagen (`Tools/Modelpunt/verify`, `Tools/Remmingwerk/verify`) blijven geldig: de
code van de tools is alleen verplaatst, niet veranderd.
