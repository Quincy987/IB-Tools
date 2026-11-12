# Verificatie "Remmingwerk" — 2026-11-12

Revit 2023.1, branch `claude-bot/t-474`, harnas `tests/RevitHarness` (job `remmingwerk.txt`) op een
**kopie** van `VibeModel/demo_out/video/kade/Prinsengracht kademuur 2.0002.rvt` (origineel niet
aangeraakt). Volledig onbemand met `tests\RevitHarness\run-remmingwerk.ps1`: testslot, deploy,
startdialogen (Unsigned Add-In ×2, "RevitShenanigans - Error") beantwoord, UI-test, Undo,
eigen Revit gesloten, slot vrijgegeven.

**Resultaat: 55 van 55 controles OK.**

## Dynamo-gedrag gemeten, niet aangenomen

Met `DynamoCLI.exe` uit Revit 2023 (Dynamo 2.16, Revit's geometriekern) een mini-graaf gedraaid:

| Dynamo-node | Uitkomst |
|---|---|
| `Line (0,0)→(1000,0)`.Offset(100) | y = −100: **rechts** van de tekenrichting |
| `Line (0,0)→(0,1000)`.Offset(100) | x = +100: rechts |
| boog tegen de klok in, Offset(100) | R 1100: naar buiten (= rechts) |
| boog met de klok mee, Offset(100) | R 1100: naar buiten (= **links** van de tekenrichting) |
| `Vector(0,1,0).AngleAboutAxis(X, Z)` | 270 → het script draait stijlen/wrijfstijlen −θ |
| `0..17000..4250` | 0, 4250, …, 17000 (eindpunt hoort erbij) |

Dus: standaard palen **rechts van de tekenrichting** = Dynamo voor rechte lijnen. Dynamo's draaiing
(−θ, afgerond) klopt alleen voor lijnen langs X; de knop draait exact mee met de lijn (θ). Voor θ = 0
zijn beide gelijk, en daar zitten de families zoals hieronder.

## Gevallen (alle maten mm, NAP-level op 0)

| Geval | Gecontroleerd | Uitkomst |
|---|---|---|
| A. recht 20 m langs +X | 5 palen/stijlen/wrijfstijlen op 1500, 5750 … 18500; paal 1105, stijl 380 rechts, wrijfstijl op de lijn; paal −20000 .. 1800, stijl 0 .. 1400; wrijfgordingen 0/700/1400 op de lijn, stalen gordingen 160 rechts; Cross-Section 180°/90°, z Justification Origin, IB_wrijfstijl 300/160(/1400) | OK, afwijking 0,000 mm |
| B. recht 17 m | palen op de einden (0 … 17000) | OK |
| C. 20 m andersom getekend | alles gespiegeld (rechts = +Y), draaiing 180° | OK |
| D. boog R 30 m (90°) | 12 palen; palen op R 31,105, wrijfstijlen op R 30,000, paal en wrijfstijl op één straal (radiaal); stalen gording een boog R 30,160 | OK, 0,000 mm |
| E. recht 3 m | 1 paal in het midden + waarschuwing "korter dan de h.o.h." | OK |
| G. 20 m, "palen links" | palen op +1105 (andere kant) | OK |
| F. zonder staaltypen (849 typen tijdelijk verwijderd, teruggedraaid) | knop meldt de 3 ontbrekende typen met het K:-pad, niets geplaatst | OK |
| F2. type bestaat niet | geweigerd, 0 elementen, model ongewijzigd (135 → 135) | OK |
| UI: echte knop (PostCommand) op lijn U | venster vooringevuld (N.A.P., SB_wrijfgording, SC_wrijfstijl, balk/kolom HEA450, **kolom ø610x12.5**), overzicht "5 palen à 21,8 m, eerste op 1,500 m"; Plaatsen → "5 buispalen … (21 elementen, afwijking max 0,000 mm)"; Undo → weer 135 | OK |

Elke plaatsing leest zelf alles terug (≤ 1 mm, draaiing ≤ 0,1°) en houdt anders niets; het harnas
controleert daarnaast tegen eigen, los uitgeschreven verwachtingen.

## Hoe de families uitkomen (bounding boxes, geval A, lijn op y = 60000, palen naar −Y)

| Onderdeel | Dwars op de lijn (achter de voorzijde) | Hoogte |
|---|---|---|
| Wrijfgording | 0 .. 160 | −150 .. 150 per rij (Origin = hart) |
| Wrijfstijl | 0 .. 160, 300 breed langs de lijn | 0 .. 1400 |
| Stalen gording HEA450 (90°) | 160 .. 600 | −150 .. 150 per rij |
| Stalen stijl HEA450 | 160 .. 600, 300 breed langs de lijn | 0 .. 1400 |
| Buispaal ø610 | 800 .. 1410 (200 speling) | −20000 .. 1800 |

Dit is precies de opbouw van het Dynamo-script. Let op, ook al zo in Dynamo: stalen gording en stalen
stijl liggen in dezelfde strook (160 .. 600), en wrijfstijl en wrijfgording ook (0 .. 160). Ze
kruisen elkaar dus in het model. Op de bestektekening zitten de wrijfstijlen (350 mm) tussen de
gordingen; die variant is bewust niet gebouwd (keuze: één stuk zoals het script).

## Beelden

- `remmingwerk-ui-window.png`: het venster zoals de knop het opent.
- `remmingwerk-ui-result.png`: het resultaatvenster.
- `remmingwerk-3d.png`: geval A in 3D (geëxporteerd door het harnas).

## Unit-tests

`dotnet test tests\FoundationProjectionToggle.Core.Tests`: 64 geslaagd (waarvan 31 nieuw voor
Remmingwerk). `dotnet test tests\IBTools.Tests`: 45 geslaagd. `.\build.ps1 -NoDeploy` voor Revit
2022, 2023, 2024 en 2025: 0 waarschuwingen.
