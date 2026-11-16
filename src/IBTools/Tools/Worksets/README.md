# Worksets

Paneel **Model** › knop **Worksets**.

## Wat doet het

Zet worksharing aan als dat nog niet zo is en maakt de IB-standaardworksets:

| Workset | Inhoud |
|---|---|
| `01_Scope_boxes_en_reference_planes` | alles wat niet op een andere workset hoort |
| `02_Shared_levels_en_grids` | levels en grids |
| `03_Linked_files` | |
| `04_Hulpconstructies` | |
| `05_Projectspecifiek_workset_01` | |
| `06_Projectspecifiek_workset_02` | |

## Vervangt

Dynamo-script **IB_Worksets v1.0** (Maikel van As, op basis van Rotterdam). Dat hernoemde de twee
standaardworksets en las de overige namen uit `IB_Worksets.xlsx`; worksharing moest al aan staan
en het had het pakket ParametricMonkey nodig. De lijst zit nu in de add-in, de Excel is niet meer
nodig.

## Wat heb je nodig

Een geopend project (geen familie, niet alleen-lezen, geen cloudmodel) waarvoor Revit worksharing
toestaat.

## Zo werkt het

1. Klik **Worksets**.
2. Is het model nog niet workshared, dan vraagt de knop eerst of worksharing aan mag. Let op:
   worksharing aanzetten kan niet ongedaan worden gemaakt en wist de lijst met ongedaan maken.
3. Bij **Ja** zet de knop worksharing aan met de IB-namen voor de twee standaardworksets
   (levels/grids → 02, de rest → 01) en maakt daarna 03 t/m 06.

## Wat krijg je

Een resultaatvenster met wat gemaakt, hernoemd of al aanwezig was, met *Kopiëren naar klembord*.
Was het model al workshared:

- met precies twee worksets: die worden hernoemd. Welke de levels en grids bevat, leest de knop af
  aan een level (of grid), niet aan de naam, dus het werkt in elke Revit-taal;
- daarna worden alleen de ontbrekende IB-worksets gemaakt. Hernoemen en maken is één stap (één
  Ctrl+Z).

Na het aanzetten van worksharing: sla het model op, dan wordt het het centrale model.

## Beperkingen

- Heeft het model al meer dan twee worksets, dan wordt niets hernoemd. Staan de levels en grids
  dan niet op `02_Shared_levels_en_grids`, dan meldt de knop dat; verplaatsen doe je zelf.
- Een workset die door iemand anders is uitgecheckt, wordt niet hernoemd (staat in de melding).
- Een nieuwe lijst met worksets betekent een nieuwe versie van IB Tools.

## Demo en test

Demo: [ibtools-demo.mp4](../../../../docs/demo/ibtools-demo.mp4), eerste deel. Getest in Revit
2023; er is geen apart verslag in de repo. Code: `CreateIbWorksetsCommand`, `WorksetService`,
`IbWorksetStandard` (de lijst).
