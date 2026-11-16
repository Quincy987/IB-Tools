# IB Tools

Revit-add-in van het **Ingenieursbureau Gemeente Amsterdam**: een bibliotheek van tools die de
Dynamo-scripts van het bureau vervangen. Eén keer installeren geeft één tabblad **IB Tools** met
alle tools; geen Dynamo Player, geen packages, geen Excel ernaast.

## Tools

| Paneel | Knop | Wat doet het | Vervangt (Dynamo) |
|---|---|---|---|
| Weergave | [Hide/Show](src/IBTools/Tools/Projectielijnen/README.md) | Projectielijnen van funderingen in de actieve view aan/uit. | Projectielijnen funderingspalen zichtbaar v1.0 |
| Coördinaten | [Paalcoördinaten](src/IBTools/Tools/Paalcoordinaten/README.md) | Schrijft de RD-coördinaten van elke paal in X-/Y-coördinaat_funderingspaal. | Funderingspalen_coordinaten v1.0 |
| Coördinaten | [Modelpunt](src/IBTools/Tools/Modelpunt/README.md) | Plaatst het IB-coördinaatpunt op een RD-coördinaat en NAP-hoogte. | Modelpoint op coordinaat v1.1 |
| Model | [Worksets](src/IBTools/Tools/Worksets/README.md) | Zet worksharing aan en maakt de IB-standaardworksets 01 t/m 06. | IB_Worksets v1.0 |
| Kademuren | [Remmingwerk](src/IBTools/Tools/Remmingwerk/README.md) | Plaatst een standaard remmingwerk langs modellijnen. | Remmingwerk v1.2 |

Elke tool heeft een eigen README: wat het doet, wat je nodig hebt, de stappen, wat je krijgt, de
verschillen met het Dynamo-script en de beperkingen, met links naar de demo-video en het
testverslag. Demo-video van Worksets, Modelpunt en Paalcoördinaten:
[docs/demo/ibtools-demo.mp4](docs/demo/ibtools-demo.mp4).

## Installeren

1. Download de zip voor **jouw Revit-versie** van de
   [nieuwste release](https://github.com/Quincy987/IB-Tools/releases/latest)
   (`IBTools-v<versie>-Revit2022.zip` … `-Revit2025.zip`). 2022, 2023 en 2024 zijn
   .NET Framework 4.8-builds, 2025 is een .NET 8-build; ze zijn niet uitwisselbaar.
2. Pak de zip uit (rechtsklik > *Alles uitpakken*) en sluit Revit.
3. Dubbelklik **`Installeren.cmd`** in de uitgepakte map. Het zet de add-in in
   `%APPDATA%\Autodesk\Revit\Addins\<jaar>\` en haalt de Windows-downloadblokkade eraf. Geeft
   Windows een beveiligingswaarschuwing, klik *Uitvoeren*; bij "Windows heeft uw pc beveiligd"
   eerst *Meer informatie* en dan *Toch uitvoeren*.
4. Start Revit en klik **Always Load** bij de vraag over de add-in *IB Tools*
   (Ingenieursbureau Gemeente Amsterdam).

Het tabblad **IB Tools** verschijnt in het lint. In de zip zit ook `LEESMIJ.txt` met deze stappen.

**Handmatig (als het script niet mag draaien):** rechtsklik op `FoundationProjectionToggle.dll` >
*Eigenschappen* > vink **Blokkering opheffen** aan > OK, en kopieer dan
`FoundationProjectionToggle.dll` en `FoundationProjectionToggle.addin` naar
`%APPDATA%\Autodesk\Revit\Addins\<jaar>\`. Sla je het deblokkeren over, dan meldt Revit
"External Tool Failure" en verschijnt het tabblad niet (getest in Revit 2023).

**Bijwerken:** precies hetzelfde; `Installeren.cmd` vervangt de vorige versie (sluit eerst Revit,
anders meldt het script dat de dll in gebruik is en verandert het niets). Vanaf v1.0.0 (toen nog
"Foundation Projection Toggle"): de knop Hide/Show verhuist van het tabblad Add-Ins (paneel
Projectielijnen) naar het tabblad **IB Tools**, paneel Weergave.

**Waarom heten de bestanden nog FoundationProjectionToggle?** Zo heette de add-in toen hij één knop
had. De naam en het add-in-id blijven bewust gelijk zodat bijwerken alleen overschrijven is; zie
[Hernoemen](#hernoemen-van-de-geleverde-bestanden-bewust-nog-niet).

## Gebouwd voor

| Revit | Framework | Status |
|---|---|---|
| 2022, 2023, 2024 | net48 | bouwt; getest in Revit 2023 |
| 2025 | net8.0-windows | bouwt; nog niet in Revit 2025 gedraaid |
| 2026 | net8.0-windows | nog niet aangezet (`2026` toevoegen aan de ValidateSet in `build.ps1`; SDK-pakket 2026 bestaat; `ElementId.IntegerValue` bestaat niet meer in 2026) |

## Indeling van de repo

```
README.md                       dit bestand
FoundationProjectionToggle.addin  manifest (wordt met de dll geïnstalleerd)
build.ps1                       bouwen en (met testslot) deployen
src/IBTools/
  App.cs                        registreert alle tools op het lint, in deze volgorde
  Shared/                       wat meerdere tools gebruiken: lint, vensters, resultaatvenster,
                                klembord, logger, families laden, invoer van coördinaten
  Tools/<Tool>/                 per tool: code, Resources/ (iconen, families), README.md,
                                verify/ (testverslagen) en demo/ (video) waar van toepassing
tests/IBTools.Tests/            unit-tests zonder Revit, map per tool
tests/RevitHarness/             integratietests in een echte Revit, map per tool
tests/Release/                  tests van Installeren.cmd (zonder Revit en in Revit 2023)
docs/                           TESTING.md, ONTWIKKELEN.md, demo/ (video van meerdere tools)
scripts/                        testslot, demo-opnames, release.ps1, release/ (Installeren.cmd, LEESMIJ.txt)
```

## Bouwen en testen

Vereist de .NET 8 SDK (die bouwt ook de .NET Framework 4.8-versies).

```powershell
dotnet build; dotnet test                # alles bouwen + unit-tests (zonder Revit)
.\build.ps1 -NoDeploy                    # alleen compileren voor Revit 2023, raakt Revit nooit aan
.\build.ps1 -NoDeploy -RevitVersion 2025
.\build.ps1                              # bouwen + deployen + Revit 2023 starten (met testslot)
.\scripts\release.ps1                    # tests + Release-zips voor 2022-2025 (publiceert niets)
```

`build.ps1` sluit nooit een Revit die het niet zelf heeft gestart. Deployen en testen in Revit gaan
om de beurt via een slotbestand: [docs/TESTING.md](docs/TESTING.md). Een nieuwe tool toevoegen:
[docs/ONTWIKKELEN.md](docs/ONTWIKKELEN.md).

Logging staat standaard uit. Aanzetten: omgevingsvariabele `IBTOOLS_DEBUG=1` vóór het starten van
Revit (of `.\build.ps1 -Debug`); logs in `%LOCALAPPDATA%\IBTools\logs\`.

## Hernoemen van de geleverde bestanden (bewust nog niet)

De code heet `IBTools` (map, project, namespaces), maar de geleverde bestanden heten nog
`FoundationProjectionToggle.dll` en `FoundationProjectionToggle.addin` en het manifest houdt zijn
`ClientId`. Gebruikers zien die naam niet: Revit toont "IB Tools". Hernoemen (bv. naar
`IBTools.dll` / `IBTools.addin`) moet één bewuste stap zijn: `Installeren.cmd` moet dan het oude
`FoundationProjectionToggle.addin` en `.dll` verwijderen, anders vindt Revit twee manifesten en
laadt de add-in dubbel of weigert hij het dubbele ClientId. Daarvoor aan te passen:

- `AssemblyName` en het opt-in deploydoel (`DeployAddin`) in `src/IBTools/IBTools.csproj`;
- de naam van het manifest en zijn `<Assembly>`;
- `$DllName`/`$ManifestName` in `build.ps1`;
- `$IBToolsDllName`/`$IBToolsAddinName` in `scripts/release/ReleaseFiles.ps1` (release.ps1, de
  installatiesjablonen via `{{DLL}}`/`{{ADDIN}}` en `tests/Release` nemen ze daaruit over), plus in
  `Installeren.template.cmd` het opruimen van de oude namen;
- de oude ontwikkel-add-in `FoundationProjectionToggle.DevTools.dll` op de ontwikkel-pc verwijst
  naar de assemblynaam `FoundationProjectionToggle`; na hernoemen werkt die niet meer, en dan kan
  ook `src/IBTools/Shared/LegacyLogger.cs` weg.
