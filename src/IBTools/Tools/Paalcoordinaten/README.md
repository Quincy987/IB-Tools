# Paalcoördinaten

Paneel **Coördinaten** › knop **Paalcoördinaten**.

## Wat doet het

Schrijft van elke paal de RD-coördinaten van het invoegpunt (in meters, 3 decimalen) in de
parameters `X-coördinaat_funderingspaal` en `Y-coördinaat_funderingspaal`.

## Vervangt

Dynamo-script **Funderingspalen_coordinaten v1.0** (Q. de Klerk, 2025, op basis van het IBR-script).
Dat nam altijd het hele model, alleen families met "paal" in de naam, en rekende zelf om via het
projectbasispunt.

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
