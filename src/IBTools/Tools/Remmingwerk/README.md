# Remmingwerk

Paneel **Kademuren** › knop **Remmingwerk**.

## Wat doet het

Plaatst langs één of meer modellijnen een standaard remmingwerk: buispalen, stalen stijlen en
stalen gordingen, houten wrijfgordingen en wrijfstijlen.

## Vervangt

Dynamo-script **Remmingwerk v1.2** (`K:\ALG\Autodesk\2023\DYNAMO\Dynamo script Revit\Remmingwerk`).

## Wat heb je nodig

- De voorzijde van het remmingwerk als één of meer horizontale **modellijnen of -bogen**.
- De staaltypen in het project: een stalen gording (Structural Framing met `NLRS_C_hoogte` en
  `NLRS_C_breedte`, bv. balk HEA450), een stalen stijl (Structural Column met `NLRS_C_hoogte`, bv.
  kolom HEA450) en een buispaal (Structural Column met `NLRS_C_diameter`, bv. kolom ø610x12.5).
  Ontbreekt er een, dan zegt de knop welk bestand je van K: moet laden.
- De houten families (`SB_wrijfgording`, `SC_wrijfstijl_generic model`) zitten in IB Tools en
  worden geladen als ze ontbreken.

## Zo werkt het

1. Teken de voorzijde. De tekenrichting bepaalt de kant: standaard komen de palen **rechts** van
   de tekenrichting, zoals in het Dynamo-script. Het venster biedt ook "links".
2. Selecteer de lijn(en) en klik **Remmingwerk**. Zonder selectie vraagt de knop je ze aan te
   wijzen.
3. Kies in het venster het NAP-level, de vijf typen en de maten. De standaardwaarden zijn die van
   het script; de laatste keuzes worden per Revit-sessie onthouden. De groene regel laat zien wat
   er geplaatst wordt, bv. "5 palen à 21,8 m, eerste op 1,500 m".
4. Klik **Plaatsen**.

## Wat krijg je

Alles staat in één stap in het model (één Ctrl+Z) en is geselecteerd. Het resultaatvenster noemt
de aantallen per onderdeel, de element-ids, de hoogtes t.o.v. NAP en de teruggelezen afwijking.
Komt een element meer dan 1 mm naast de berekende plek, dan wordt niets geplaatst.

Wat de knop berekent (mm; n rijen, a = h.o.h. rijen, b = breedte wrijfgording, p = h.o.h. palen):

- palen, stijlen en wrijfstijlen op floor(L/p)+1 posities, p uit elkaar, gecentreerd op de lijn;
- wrijfgordingen op de lijn op 0, a … (n−1)a;
- stalen gordingen b naar de paalkant;
- stalen stijlen b + ½·stijlhoogte naar de paalkant, van de lijnhoogte tot (n−1)a erboven;
- buispalen gordinghoogte + b + ½D + 200 naar de paalkant, van de paalpunt (t.o.v. het NAP-level)
  tot (n−1)a + ½·gordingbreedte + overlengte boven de lijn;
- wrijfstijlen op de lijn, lengte (n−1)a.

## Verschillen met Dynamo

Bewust anders dan het script:

- De paalpunt geldt t.o.v. het NAP-level (Dynamo: t.o.v. de lijn). Bij een lijn op NAP 0 is de
  uitkomst gelijk.
- Stijlen en wrijfstijlen draaien precies mee met de lijn. Dynamo rondde de hoek af en spiegelde
  hem, wat alleen klopt voor lijnen langs de X-as.
- Op een boog liggen paal, stijl en wrijfstijl op één straal, en "rechts" is altijd rechts van de
  tekenrichting. Dynamo zette bij elke boog alles naar buiten.

## Beperkingen

- Net als in het script liggen stalen gording en stalen stijl in dezelfde strook (160 – 600 mm
  achter de voorzijde), en wrijfstijl en wrijfgording ook (0 – 160 mm); ze snijden elkaar in het
  model.
- De wrijfstijlen zijn één stuk over de hele hoogte, zoals in het script. Op de bestektekening
  zitten stukken van 350 mm tussen de gordingen; die variant is niet gebouwd.
- Een boog die aan de paalkant te krap is voor de afstanden, wordt geweigerd.

## Demo en test

- Demo: [demo/remmingwerk-demo.mp4](demo/remmingwerk-demo.mp4) (75 s), stills in `demo/stills`.
- Verslag: [verify/remmingwerk-2026-11-12.md](verify/remmingwerk-2026-11-12.md) (Revit 2023, 55/55
  controles).
- Opnieuw testen in Revit: `tests\RevitHarness\Remmingwerk\run-remmingwerk.ps1`; de rekenkern (`Core/`) heeft
  unit-tests in `tests/IBTools.Tests/Remmingwerk`.
- Code: `RemmingwerkCommand`, `RemmingwerkService`, `RemmingwerkFamilyService`, venster
  `RemmingwerkWindow`.
