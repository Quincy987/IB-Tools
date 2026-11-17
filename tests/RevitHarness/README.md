# Revit-testharnas (IB Tools)

Integratietests in een echte Revit 2023, één job per tool. Geen onderdeel van de add-in.
`HarnessApp.cs` is het startpunt (en de Modelpunt-job); de bestanden per job staan in een submap
(`Modelpunt/`, `Remmingwerk/`, `Paalcoordinaten/`).

## Modelpunt op coördinaat

Het harnas doet alleen iets als `IBTOOLS_HARNESS_JOB` naar een (lege) jobmap wijst; een andere Revit
die het toevallig laadt, merkt er niets van.

Wat het doet (`HarnessApp.cs`), op een **kopie** van een model:

- A. hoek van de kademuur → gedeelde coördinaten zoals Report Shared Coordinates ze geeft → punt plaatsen → teruglezen (≤ 1 mm);
- B. weigermelding zonder RD-coördinaten (teruggedraaid);
- C. variant: ware noorden +10° en projectbasispunt 100 m / 40 m van de interne oorsprong → A opnieuw, plus waar de Dynamo-formule zou uitkomen;
- D. de echte knop (PostCommand) met de tweede hoek op het klembord; `Modelpunt/ui-test.ps1` controleert en bevestigt het venster via UI Automation.

Resultaten: `results.txt`, `ui-result.txt`, screenshots en de opgeslagen variant in de jobmap.

## Draaien (met het testslot, zie docs/TESTING.md)

```powershell
dotnet build tests\RevitHarness -c Release
$job = "$env:LOCALAPPDATA\IBTools\test\modelpunt\job"; New-Item -ItemType Directory -Force $job
$env:IBTOOLS_HARNESS_JOB = $job
$addins = "$env:APPDATA\Autodesk\Revit\Addins\2023"
Copy-Item tests\RevitHarness\bin\Release\IBTools.RevitHarness.dll, tests\RevitHarness\IBTools.RevitHarness.addin $addins
.\build.ps1 -Deploy -OpenFile "<kopie van het model>.rvt"     # neemt het slot, start Revit
# zodra started.txt er is: manifest weer weghalen, zodat andere sessies het niet laden
Remove-Item "$addins\IBTools.RevitHarness.addin"
.\tests\RevitHarness\Modelpunt\ui-test.ps1 -Job $job -RevitPid (Get-Content "$job\started.txt")
# wacht op done.txt, dan:
.\scripts\revit-lock.ps1 release; Remove-Item "$addins\IBTools.RevitHarness.dll"
```

## Remmingwerk

Zelfde harnas, andere job: een jobmap met `remmingwerk.txt` draait `Remmingwerk/RemmingwerkHarness.cs`.
Alles in één script (testslot, kopie van het kademodel, staaltypen van K: of de lokale cache,
harnas deployen/weghalen, UI-test, `release` in `finally`). Het slot komt **eerst**: is het bezet
door een andere sessie, dan stopt het script met exit 2 zonder iets aan te raken (geen jobmap, niets
in de Addins-map):

```powershell
$env:IBTOOLS_LOCK_OWNER = "<threadnaam>"
.\tests\RevitHarness\Remmingwerk\run-remmingwerk.ps1
```

Wat het controleert (`%LOCALAPPDATA%\IBTools\test\remmingwerk\job\results.txt`, regels `OK`/`FOUT`):

- F. zonder staaltypen meldt de knop vóór het venster de 3 ontbrekende typen met hun K:-pad. Heeft het
  model ze al, dan haalt het harnas ze tijdelijk weg (transactie wordt teruggedraaid); heeft het ze
  niet, dan laadt het daarna HEA450-balk, HEA450-kolom en buis ø610x12.5 uit `job\staal`;
- A recht 20 m, B 17 m (palen op de einden), C 20 m andersom getekend, D boog R 30 m (radiaal),
  E 3 m (1 paal + waarschuwing), G palen links: posities tegen onafhankelijk uitgeschreven
  verwachtingen (≤ 1 mm), hoogtes t.o.v. NAP, Cross-Section Rotation, z Justification en de
  IB_wrijfstijl-parameters; bounding boxes per onderdeel in het log;
- F2. een type dat niet bestaat: geweigerd, 0 elementen;
- UI: lijn U geselecteerd, echte knop (PostCommand); `Remmingwerk/ui-test-remmingwerk.ps1` leest het venster
  uit, klikt Plaatsen, leest het resultaat en doet Undo; het harnas telt na afloop weer het
  aantal van vóór de knop.

Beelden in de jobmap: `remmingwerk-3d*.png` (geëxporteerde 3D-view), `ui-window.png`,
`ui-result.png`, `ui-model-after.png`.

## Paalcoördinaten (automatisch bijwerken)

Jobmap met `paalcoordinaten.txt` draait `Paalcoordinaten/PaalcoordinatenHarness.cs`. Alles in één
script, met het slot eerst (bezet = exit 2, niets aangeraakt) en `release` in `finally`:

```powershell
$env:IBTOOLS_LOCK_OWNER = "<threadnaam>"
.\tests\RevitHarness\Paalcoordinaten\run-paalcoordinaten.ps1        # -SkipH: zonder stap H
```

Het script kopieert het IB-template en `NLI_Funderingspaal.rfa` (van K:, anders de cache
`%LOCALAPPDATA%\IBTools\paalcoordinaten-src`) en start Revit 2023 met debug-log (`build.ps1 -Deploy
-Debug`). Het harnas maakt zelf een nieuw project uit het template en controleert
(`%LOCALAPPDATA%\IBTools\test\paalcoordinaten\job\results.txt`, regels `OK`/`FOUT`; verwachte
waarden komen uit Revits eigen *Report Shared Coordinates*, niet uit de omrekening van de add-in):

- updater geregistreerd en optioneel;
- F. model niet op coördinaten: plaatsen en verplaatsen schrijft niets, één logregel;
- A0. op coördinaten zetten: de updater werkt alle palen bij;
- A. alle X met de hand fout zetten (blijft staan: de updater reageert niet op parameters), dan
  de service voor het hele model zoals de knop: alles geschreven; I. tweede keer: knop-rapport
  "Alles ongewijzigd";
- B. paal verplaatsen: waarden volgen, precies één updater-run (geen lus); C. kopie; D. nieuwe paal;
- E1. projectlocatie wijzigen, E2. meetpunt verplaatsen: alle palen kloppen weer;
- G1-G4, via heropenen in de UI (ExternalEvent): "Niet nu" laat de waarde staan; de echte vraag
  wordt door `Paalcoordinaten/ui-test-paalcoordinaten.ps1` aangeklikt ("Nu bijwerken") en het
  rapport gesloten; daarna geen vraag meer; met `IBTOOLS_PILECHECK=0` geen vraag maar wel de telling
  in het log;
- H. daarna sluit het script zijn eigen Revit, zet het manifest van IB Tools opzij (binnen het slot,
  tot het harnas van die Revit klaar is) en opent het opgeslagen model in een Revit zonder IB Tools:
  geen melding over een ontbrekende updater (`Paalcoordinaten/ZonderIbToolsHarness.cs`,
  `job-h\results.txt`).

Exitcode: 0 als alles OK is, 1 bij een FOUT of een afgebroken run, 2 als het testslot bezet is.

Beelden in de jobmap: `ui-vraag.png` (de vraag bij openen), `ui-rapport.png` (het rapport na
"Nu bijwerken").
