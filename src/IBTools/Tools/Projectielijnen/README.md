# Projectielijnen (Hide/Show)

Paneel **Weergave** › knop **Hide** / **Show**.

## Wat doet het

Zet in de actieve view de projectielijnen van **Structural Foundations** (subcategorie
`<Projection>`) aan of uit, zonder dat je Visibility/Graphics in hoeft. De knop laat zien wat er
gebeurt als je klikt: **Hide** met een open oog als de lijnen zichtbaar zijn, **Show** met een
gesloten oog als ze verborgen zijn. Bij elke andere view past de knop zich aan.

## Vervangt

Dynamo-script **Projectielijnen funderingspalen zichtbaar v1.0**. Dit is de knop van de eerste
release (v1.0.0, toen nog "Foundation Projection Toggle" op een eigen tabblad) en is al bij
gebruikers uitgerold.

## Wat heb je nodig

Een view met Visibility/Graphics: plattegrond, plafondplan, doorsnede, aanzicht of 3D-view.

## Zo werkt het

1. Open de view.
2. Klik **Hide** of **Show**.

## Wat krijg je

De projectielijnen van de funderingen in die ene view gaan uit of aan. Andere views veranderen
niet. Ctrl+Z maakt het ongedaan. Er komt geen melding; de knop zelf is de terugkoppeling.

## Beperkingen

- Uitgeschakeld (grijs) in views zonder Visibility/Graphics (schedules, sheets, legends) en als
  een view template V/G vastzet.
- De naam van de subcategorie verschilt per Revit-taal; de knop herkent Engels, Nederlands, Duits,
  Spaans, Italiaans, Portugees, Chinees en Japans, en zoekt anders op patroon.

## Demo en test

Geen demo-video, bewust: deze knop is al uitgerold en bekend. Code: `ToggleProjectionCommand`,
`CategoryVisibilityService`, `ProjectionToggleButton` (de knopstatus).
