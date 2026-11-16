# Modelpunt op coördinaat

Paneel **Coördinaten** › knop **Modelpunt**.

## Wat doet het

Plaatst het IB-coördinaatpunt (familie `IB_GM_coordinaatpunt`) op een opgegeven RD-coördinaat en
NAP-hoogte, via de gedeelde coördinaten van het project.

## Vervangt

Dynamo-script **Modelpoint op coordinaat v1.1** (Ingenieursbureau, 2022).

## Wat heb je nodig

- Een project dat op RD-coördinaten staat (projectbasispunt/survey point ingesteld of Acquire
  Coordinates). Zonder RD-coördinaten weigert de knop.
- De familie hoeft niet in het project te zitten: de knop laadt hem uit IB Tools (nooit van het
  netwerk).

## Zo werkt het

1. Kopieer eventueel een coördinaat, bv. uit Excel: `x y` of `x y z`, met komma of punt als
   decimaalteken, in meters of millimeters.
2. Klik **Modelpunt**. Het venster vult zich vanaf het klembord. Plakken in één van de velden of
   **Plakken (x y z)** werkt ook.
3. Controleer RD X (oost), RD Y (noord) en de hoogte in m NAP. Het type (schaal) volgt uit de schaal
   van de view.
4. Klik **Plaatsen**.

## Wat krijg je

Een resultaatvenster met de gevraagde en de teruggelezen coördinaten, Revits eigen omrekening, de
afwijking in mm, het type en het element-id, plus de controles van de gedeelde coördinaten. Het
nieuwe punt is geselecteerd. Eén Ctrl+Z maakt alles ongedaan, ook het laden van de familie.

## Verschillen met Dynamo

- Het script rekende zelf om via het projectbasispunt en "Angle to True North". In een gedraaid
  project kwam het punt daardoor tot ruim 18 m naast de bedoelde plek (gemeten, zie het verslag).
  De knop gebruikt de gedeelde coördinaten van Revit zelf en controleert het resultaat.
- Het script vroeg X en Y in millimeters en had geen invoer voor de hoogte; de knop neemt meters of
  millimeters en een NAP-hoogte, en leest ze van het klembord.
- Het type koos je in het script zelf; de knop kiest het bij de schaal van de view. De familie hoefde
  in het script al in het project te zitten; de knop laadt hem zo nodig.

## Beperkingen

- Vóór het plaatsen controleert de knop de omrekening tegen het basispunt en het survey point.
  Klopt die niet, of komt het punt na plaatsen meer dan 1 mm naast de gevraagde plek, dan wordt
  niets geplaatst.
- Waarden die alleen gedeeld door 1000 in het RD-bereik vallen, worden als millimeters gelezen en
  omgerekend; de knop laat dat eerst zien voor je plaatst.
- Ligt het punt meer dan 5 km van het projectbasispunt, dan vraagt de knop eerst of het klopt.

## Demo en test

- Demo: [ibtools-demo.mp4](../../../../docs/demo/ibtools-demo.mp4), tweede deel.
- Verslag: [verify/modelpunt-2026-11-11.md](verify/modelpunt-2026-11-11.md) (Revit 2023, ook met een
  gedraaid en verschoven project; afwijking 0,000 mm).
- Code: `PlaceCoordinatePointCommand`, `SharedCoordinateService`/`SharedCoordinateMath` (omrekening,
  unit-tests), `CoordinatePointFamilyService`, venster `CoordinateInputWindow`.
