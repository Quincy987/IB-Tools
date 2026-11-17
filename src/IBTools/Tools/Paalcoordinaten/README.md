# Paalcoördinaten

Paneel **Coördinaten** › knop **Paalcoördinaten**.

## Wat doet het

Schrijft van elke paal de RD-coördinaten van het invoegpunt (in meters, 3 decimalen) in de
parameters `X-coördinaat_funderingspaal` en `Y-coördinaat_funderingspaal`.

## Vervangt

Dynamo-script **Funderingspalen_coordinaten v1.0** (2025, op basis van het IBR-script).

## Wat heb je nodig

- Palen (Structural Foundations) met de parameters `X-coördinaat_funderingspaal` en
  `Y-coördinaat_funderingspaal` per paal, zoals in de IB-template.
- Een project op RD-coördinaten. Staat het model niet op coördinaten, dan schrijft de knop toch,
  maar waarschuwt dat de waarden dan geen RD-coördinaten zijn.

## Zo werkt het

1. Selecteer de palen die je wilt bijwerken, of selecteer niets voor het hele model.
2. Klik **Paalcoördinaten**.

## Wat krijg je

Een rapport per paal: geschreven (oude → nieuwe waarde), ongewijzigd of overgeslagen met de reden,
met *Kopiëren naar klembord*. Alleen waarden die echt veranderen worden geschreven, in één stap
(één Ctrl+Z). Een lengteparameter krijgt de meterwaarde in de lengte-eenheid van het project, net
als in het Dynamo-script, zodat de palenstaat dezelfde getallen blijft tonen.

## Automatisch bijwerken

Naast de knop houdt IB Tools de coördinaten zelf bij, zodat er geen oude waarden op tekening komen:

- **Paal verplaatst, gekopieerd of nieuw geplaatst**: de paal krijgt direct de juiste coördinaten,
  in dezelfde stap als de verplaatsing (één Ctrl+Z draait beide terug). Er komt geen venster.
- **Coördinatenstelsel gewijzigd** (projectbasispunt, meetpunt of projectlocatie, bijvoorbeeld via
  *Specify Coordinates*): alle palen worden bijgewerkt, op dezelfde manier.
- **Model openen**: IB Tools controleert of er palen zijn waarvan de opgeslagen coördinaten niet
  meer kloppen met hun plek (bijvoorbeeld verschoven door iemand zonder IB Tools, of nog nooit
  ingevuld; lege parameters tellen ook mee, dus een model dat nooit coördinaten had wordt één keer
  gevraagd). Zo ja, dan vraagt een venster *Paalcoördinaten bijwerken?* met **Nu bijwerken**
  (schrijft alles in één stap en toont daarna het rapport) of **Niet nu** (verandert niets; bij de
  volgende keer openen komt de vraag terug). Palen die nu niet kunnen (uitgecheckt door een collega
  e.d.) worden in de vraag apart geteld.

Dit gebeurt **alleen in een model op coördinaten** (gedeelde coördinaten niet gelijk aan de interne)
waarvan de zelftest op het projectbasispunt slaagt. In een model dat niet op coördinaten staat
wordt niets automatisch geschreven en komt er geen vraag; de knop werkt daar wel, met de
waarschuwing.

Dezelfde regels als bij de knop: alleen waarden die echt veranderen worden geschreven, en een paal
wordt overgeslagen om de redenen onder *Beperkingen*. Een paal waarvan je de coördinaat-parameter
met de hand wijzigt, wordt niet meteen overschreven; dat vangt de controle bij het openen op.

Collega's zonder IB Tools openen zo'n model gewoon: de automatische update is *optioneel*
geregistreerd en geeft bij hen geen melding.

Voor onbemande tests: met de omgevingsvariabele `IBTOOLS_PILECHECK=0` komt de vraag bij openen niet
(de controle draait wel en staat in het log). `build.ps1` zet die voor de Revit die het start.

## Verschillen met Dynamo

- Het script nam altijd het hele model; de knop werkt ook op alleen de geselecteerde palen.
- Het script nam alleen families met "paal" in de naam; de knop neemt elke fundering die de twee
  parameters heeft.
- Het script rekende zelf om via het projectbasispunt; de knop gebruikt de omrekening van Revit en
  controleert die eerst op het basispunt.
- De knop schrijft alleen wat verandert en geeft een rapport per paal.
- Het script moest je zelf draaien; IB Tools werkt verplaatste, gekopieerde en nieuwe palen zelf bij
  en vraagt bij het openen of verouderde palen bijgewerkt moeten worden.

## Beperkingen

Een paal wordt overgeslagen (en in het rapport genoemd) als:

- een van de twee parameters ontbreekt, of op het type staat in plaats van per paal;
- het parametertype niet wordt ondersteund, of de parameter alleen-lezen is (formule, vergrendeld);
- de paal in een groep zit en de parameter niet per groep mag verschillen;
- de paal in een workshared model door iemand anders is uitgecheckt;
- de paal geen invoegpunt heeft.

Vooraf rekent de knop het projectbasispunt om als zelftest. Wijkt dat meer dan 1 mm af van zijn
eigen E/W en N/S, dan schrijft hij niets.

## Demo en test

Demo: [ibtools-demo.mp4](../../../../docs/demo/ibtools-demo.mp4), derde deel. Getest in Revit 2023;
er is geen apart verslag in de repo. De rekenkern (`Core/`) heeft unit-tests in
`tests/IBTools.Tests/Paalcoordinaten`. Code: `WritePileCoordinatesCommand`, `PileCoordinateService`.
