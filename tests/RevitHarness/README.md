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
