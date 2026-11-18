# Verificatie "Paalcoördinaten automatisch bijwerken" — 2026-11-17

Revit 2023.1, branch `claude-bot/t-597`, harnas `tests/RevitHarness` (job `paalcoordinaten.txt`,
daarna `zonder-ibtools.txt`). Volledig onbemand met
`tests\RevitHarness\Paalcoordinaten\run-paalcoordinaten.ps1`: testslot, deploy met debug-log,
startdialogen (Unsigned Add-In ×2) beantwoord, UI-test, eigen Revit gesloten, tweede Revit zonder
IB Tools, manifest teruggezet, slot vrijgegeven.

Testmodel: een nieuw project uit een kopie van `IB_NLCS_2023_template.rte` (K:) met
`NLI_Funderingspaal.rfa` (K:, type *Betonpaal 180x180*, level-based). De X-/Y-parameters komen uit
de familie zelf (gedeelde parameters `0e4e2f36…` en `0bd0d5fb…`, lengte; project in mm). Verwachte
waarden komen uit Revits eigen *Report Shared Coordinates* (`GetProjectPosition`), niet uit de
omrekening van de add-in.

**Resultaat: 47 van 47 controles OK in de Revit met IB Tools, 2 van 2 in de Revit zonder.**
Na de review opnieuw gedraaid met dezelfde uitkomst.

## Gevallen

| Geval | Wat | Uitkomst |
|---|---|---|
| – | Updater geregistreerd, en optioneel; de paalfamilie heeft beide parameters | OK |
| F | Model **niet** op coördinaten: 4 palen plaatsen, één verplaatsen | Niets geschreven (familiewaarden 0.000 blijven), één logregel voor twee transacties |
| A0 | Model op coördinaten zetten (ware noorden 10°, interne oorsprong op RD 121000 / 487000, basispunt 100 m / 40 m verschoven) | De updater werkt alle 4 palen bij, gelijk aan Report Shared Coordinates |
| A | Alle X met de hand fout gezet, dan de service voor het hele model (wat de knop doet) | De handmatige waarde blijft staan (een parameterwijziging start de updater niet); daarna 4 geschreven, alle 4 goed |
| I | Nog een keer (knop-rapport) | "Alles ongewijzigd: 4 palen (hele model)." |
| B | Paal 3,21 m / −1,5 m verplaatsen | Nieuwe waarden direct goed; precies één updater-run in het log (geen lus) |
| C | Paal kopiëren | Kopie heeft eigen, juiste waarden |
| D | Nieuwe paal plaatsen | Juiste waarden |
| E1 | Projectlocatie wijzigen (+10 m / −4 m, +5°) | Alle 6 palen bijgewerkt via de updater (stil, zonder vraag) |
| E2 | Meetpunt verplaatsen | Alle 6 palen bijgewerkt (X van paal 1 ging van 121010.707 naar 121004.463) |
| G1 | Model opslaan met 1 foute X, heropenen in de UI, "Niet nu" | Eén vraag; log "6 palen met coördinaatparameters, 1 verouderd (1 schrijfbaar, 0 geblokkeerd)"; X blijft fout |
| G2 | Heropenen, **echte klik** op "Nu bijwerken" (UI-test) | Vraag getoond, X hersteld, rapport "1 paal geschreven, 5 ongewijzigd, 0 overgeslagen (hele model)." |
| G3 | Opnieuw heropenen | Geen vraag (niets te schrijven) |
| G4 | Weer 1 foute X, heropenen met `IBTOOLS_PILECHECK=0` | Geen vraag, telling wel in het log, niets geschreven |
| H | Het opgeslagen model (waarin de updater schreef) openen in een Revit **zonder** IB Tools | IB-updater niet geregistreerd; 0 dialogen bij openen, dus geen melding over een ontbrekende updater |

Beelden: [de vraag bij openen](paalcoordinaten-vraag-bij-openen.png) en
[het rapport na "Nu bijwerken"](paalcoordinaten-rapport-na-bijwerken.png).

## Wat het coördinatenstelsel betreft

Plan was: stil bijwerken als het stelsel wijzigt, en anders terugvallen op de vraag. In Revit 2023
werkt het stille pad: de wijziging van projectlocatie, projectbasispunt of meetpunt komt via de
updater binnen in dezelfde transactie (E1, E2, A0), dus er is geen terugval nodig.

## Onderweg gevonden (harnas, niet de add-in)

- Revit toont bij het op coördinaten zetten de waarschuwing "No columns are joined to grid lines…".
  Wie die via DialogBoxShowing met OK beantwoordt, draait de hele transactie terug. De
  harnas-transacties ruimen waarschuwingen nu zelf op en zetten ze in het verslag.
- Revit zet de naam van de add-in voor de titel van een TaskDialog: de vraag heet daarom
  "Paalcoördinaten" en staat in beeld als "IB Tools - Paalcoördinaten".

## Ctrl+Z (bij de demo-opname, 2026-11-18)

Bij het opnemen van `demo/paalcoordinaten-demo.mp4` op het kademodel Prinsengracht (40 houten palen
op RD) is Paal A 2 m verplaatst: X 121108.435 → 121110.382. Eén Ctrl+Z zette de paal én de
coördinaten terug (X 121108.435), Ctrl+Y zette beide weer op de nieuwe plek. De waarden zijn na
elke stap teruggelezen uit het model. Een kopie van Paal A kreeg eigen coördinaten (X 121113.302).

## Niet getest

- Revit 2022, 2024 en 2025: alleen gecompileerd (op deze pc staat alleen Revit 2023).
- Een gedeeld model (worksharing) met palen die door een collega zijn uitgecheckt: de regels zijn
  dezelfde als die van de knop. De telling "geblokkeerd" in de vraag is alleen met unit-tests gedekt.
- Een echt projectmodel van het bureau: de test gebruikt het IB-template met de IB-paalfamilie.
- Een tegenproef voor H: dat een *niet*-optionele updater in hetzelfde geval wél een melding geeft,
  is niet gedraaid. H laat alleen zien dat er met deze (optionele) updater geen melding komt.
- Andere manieren om het coördinatenstelsel te wijzigen dan E1/E2: *Acquire Coordinates* van een
  gekoppeld model en een andere projectlocatie actief maken (*Make Current*). Gaat dat buiten de
  updater om, dan vangt de controle bij de volgende keer openen het op.
