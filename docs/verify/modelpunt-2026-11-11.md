# Verificatie "Modelpunt op coördinaat" — 2026-11-11

Revit 2023.1, branch `feature/modelpunt-coordinaat`, harnas `tests/RevitHarness` op een **kopie** van
`VibeModel/demo_out/video/kade/Prinsengracht kademuur.rvt` (origineel niet aangeraakt).

Het model heeft RD-coördinaten: basispunt op de interne oorsprong = RD 121117.767 / 486171.651,
survey point op RD 0 / 0, ware noorden 0°. `GetTotalTransform()` geeft hier **gedeeld → intern**
(origin −121117.767; −486171.651), dus de gemeten richting is nodig: de knop gebruikte de inverse.

| Stap | Resultaat |
|---|---|
| A. Origineel: hoek van de bestrating (intern −120.534; 14.211; 1.000) → Report Shared Coordinates 120997.233 / 486185.862 / 1.000 → punt plaatsen → teruglezen | 120997.233 / 486185.862 / 1.000, **0.000 mm** |
| B. Zonder RD (basispunt op 0/0): knop | weigert: "Dit project heeft geen RD-coördinaten: het projectbasispunt staat op E/W 0.000, N/S 0.000 m …" (teruggedraaid) |
| C. Variant: ware noorden +10°, basispunt 100 m / 40 m van de oorsprong; zelfde hoek → 120996.596 / 486164.716 / 1.000 | teruggelezen gelijk, **0.000 mm**; draaiing 10.0000° = locatie; basispunt en survey point 0.000 mm |
| C. Dynamo-formule v1.1 op dezelfde hoek | 18.8 m (of 58.7 m, andere draairichting) naast de hoek |
| D. Echte knop, klembord "121124,907 486164,311 1,300" (hoek 2, decimale komma's) | venster vooringevuld, type uit de view-schaal (1op200); punt 1731112 op 0.296 mm van hoek 2 (klembord op 1 mm afgerond), teruggelezen 121124.907 / 486164.311 / 1.300, afwijking 0.000 mm |

Schermafdrukken: `modelpunt-ui-window.png`, `modelpunt-ui-result.png`.

Opmerkingen:
- Een tweede punt op precies dezelfde plek geeft de normale Revit-waarschuwing "identical instances";
  de plaatsing gaat gewoon door.
- Elke nieuwe (ongesigneerde) build vraagt bij de start van Revit "Load Once/Always"; het harnas
  beantwoordt nu zelf de Revit-dialogen die daarna komen (unresolved links, waarschuwingen).
- Unit-tests: `dotnet test tests\IBTools.Tests` — 45 geslaagd.
