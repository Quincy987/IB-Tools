# Revit-testharnas (IB Tools)

Integratietest in een echte Revit 2023 voor **Modelpunt op coördinaat**. Geen onderdeel van de add-in.
Het harnas doet alleen iets als `IBTOOLS_HARNESS_JOB` naar een (lege) jobmap wijst; een andere Revit
die het toevallig laadt, merkt er niets van.

Wat het doet (`HarnessApp.cs`), op een **kopie** van een model:

- A. hoek van de kademuur → gedeelde coördinaten zoals Report Shared Coordinates ze geeft → punt plaatsen → teruglezen (≤ 1 mm);
- B. weigermelding zonder RD-coördinaten (teruggedraaid);
- C. variant: ware noorden +10° en projectbasispunt 100 m / 40 m van de interne oorsprong → A opnieuw, plus waar de Dynamo-formule zou uitkomen;
- D. de echte knop (PostCommand) met de tweede hoek op het klembord; `ui-test.ps1` controleert en bevestigt het venster via UI Automation.

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
.\tests\RevitHarness\ui-test.ps1 -Job $job -RevitPid (Get-Content "$job\started.txt")
# wacht op done.txt, dan:
.\scripts\revit-lock.ps1 release; Remove-Item "$addins\IBTools.RevitHarness.dll"
```

## Remmingwerk

Zelfde harnas, andere job: een jobmap met `remmingwerk.txt` draait `RemmingwerkHarness.cs`.
Alles in één script (testslot, kopie van het kademodel, staaltypen van K: of de lokale cache,
harnas deployen/weghalen, UI-test, `release` in `finally`):

```powershell
$env:IBTOOLS_LOCK_OWNER = "<threadnaam>"
.\tests\RevitHarness\run-remmingwerk.ps1
```

Wat het controleert (`%LOCALAPPDATA%\IBTools\test\remmingwerk\job\results.txt`, regels `OK`/`FOUT`):

- F. zonder staaltypen weigert de knop vóór het venster (3 ontbrekende typen gemeld); daarna worden
  HEA450-balk, HEA450-kolom en buis ø610x12.5 uit `job\staal` geladen;
- A recht 20 m, B 17 m (palen op de einden), C 20 m andersom getekend, D boog R 30 m (radiaal),
  E 3 m (1 paal + waarschuwing), G palen links: posities tegen onafhankelijk uitgeschreven
  verwachtingen (≤ 1 mm), hoogtes t.o.v. NAP, Cross-Section Rotation, z Justification en de
  IB_wrijfstijl-parameters; bounding boxes per onderdeel in het log;
- F2. een type dat niet bestaat: geweigerd, 0 elementen;
- UI: lijn U geselecteerd, echte knop (PostCommand); `ui-test-remmingwerk.ps1` leest het venster
  uit, klikt Plaatsen, leest het resultaat en doet Undo; het harnas telt na afloop weer het
  aantal van vóór de knop.

Beelden in de jobmap: `remmingwerk-3d*.png` (geëxporteerde 3D-view), `ui-window.png`,
`ui-result.png`, `ui-model-after.png`.
