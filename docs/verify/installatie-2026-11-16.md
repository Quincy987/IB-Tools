# Testverslag: Installeren.cmd in de release-zips (16-11-2026)

Branch `claude-bot/t-588`, versie 2.1.0. Elke release-zip bevat nu precies vier bestanden:
`FoundationProjectionToggle.dll`, `FoundationProjectionToggle.addin`, `Installeren.cmd` en
`LEESMIJ.txt`. Er is geen add-in-code veranderd (niets onder `src/`).

## 1. Release-zips (`scripts/release.ps1`)

Unit-tests geslaagd, builds 2022–2025 zonder waarschuwingen. Per zip zijn de vier bestanden
gecontroleerd (het script stopt als het er andere zijn).

```
IBTools-v2.1.0-Revit2022.zip 1064 KB   (.NETFramework,Version=v4.8, dll 2.1.0.0)
IBTools-v2.1.0-Revit2023.zip 1064 KB   (.NETFramework,Version=v4.8, dll 2.1.0.0)
IBTools-v2.1.0-Revit2024.zip 1064 KB   (.NETFramework,Version=v4.8, dll 2.1.0.0)
IBTools-v2.1.0-Revit2025.zip 1065 KB   (.NETCoreApp,Version=v8.0, dll 2.1.0.0)
```

## 2. Installeren.cmd zonder Revit (`tests/Release/test-install.ps1`): 53/53

De bestanden uit elke zip zijn gemarkeerd als download (`Zone.Identifier`, ZoneId=3), zoals
Windows doet bij het uitpakken van een gedownloade zip. Uitgepakt in een pad met spaties,
haakjes en een `&` (`Mijn downloads 2023\IBTools (1) & co`). Elke run heeft een eigen APPDATA
en een nagemaakte Program Files in %TEMP%. Het script is gestart zoals de Verkenner het doet
(`cmd.exe /c ""<pad>""`).

- **Per zip:** precies de vier bestanden, alle vier gemarkeerd als download.
- **2022, 2023, 2024:** .NET 4.8 weigert de gemarkeerde dll (`FileLoadException`). Na het
  installeren staan beide bestanden in `Addins\<jaar>`, zonder blokkade en gelijk aan de zip. Er
  blijven geen restbestanden achter. De geïnstalleerde dll laadt wél in .NET 4.8. Een tweede run
  (bijwerken) eindigt met exit 0.
- **2025:** zelfde controles, zonder de .NET 4.8-laadtest.
- **Revit 2023 niet geïnstalleerd** (alleen Revit 2024 aanwezig): zonder antwoord en met antwoord
  N exit 3, niets gekopieerd. De melding noemt "Wel gevonden: Revit 2024". Met antwoord J exit 0
  en geïnstalleerd.
- **dll in gebruik** (een .NET 4.8-proces heeft hem geladen, zoals een draaiende Revit): exit 4
  met "Sluit Revit 2023". Bestanden en tijdstempel zijn onveranderd en er is niets bijgekomen.
- **Fout halverwege** (de dll is al omgewisseld als de stap voor het manifest mislukt): exit 5 met
  "Er is niets veranderd". De vorige dll en het vorige manifest staan er weer en er zijn geen
  restbestanden.
- **IB Tools staat al op een andere plek** (een manifest met dezelfde ClientId in een nagemaakte
  ProgramData, of onder een andere naam in de eigen map): exit 6. De melding noemt het bestand en
  er is niets gekopieerd. Een ander add-in in ProgramData (andere ClientId) houdt de installatie
  niet tegen; dat zit in alle gewone runs.
- **Zip niet uitgepakt** (alleen het cmd-bestand in een map): exit 2 met "Pak eerst de zip uit".

Gevonden en opgelost tijdens het testen: de markering `##PS-START` stond ook in een
commentaarregel bovenin, waardoor PowerShell op de verkeerde plek begon. De markering is uit het
commentaar gehaald, en `ReleaseFiles.ps1` weigert nu een cmd-bestand waarin hij niet precies één
keer staat.

Gevonden bij de review en opgelost:

- **Terugzetten:** als het verplaatsen van een bestaand bestand mislukte, verwijderde het
  terugzetten dat bestand. Nu wordt alleen teruggedraaid wat echt verplaatst of neergezet is.
  Ook het kopiëren valt nu onder het terugzetten. Dit wordt getest met "Fout halverwege".
- **PowerShell:** het script roept PowerShell aan met het volledige pad
  (`%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe`), dus niet meer via PATH.
- **Controle op tekens:** `ReleaseFiles.ps1` weigert nu ook stuurtekens in de sjablonen. Een
  bewerking had `\v` in dat pad in een stuurteken veranderd.
- **Bestandsnamen:** de namen van de geleverde bestanden staan op één plek (`ReleaseFiles.ps1`),
  ook voor de sjablonen en de tests.
- **Melding na terugzetten:** "Er is niets veranderd" verschijnt alleen als alles echt terug is.
  Anders zegt de melding welk bestand nog als `.ibtools-old` in de map staat.
- **Tweede installatie:** Hide/Show is al bij gebruikers uitgerold. Stond die installatie voor alle
  gebruikers in ProgramData (of onder een andere naam), dan zette het script er een tweede
  IB Tools met dezelfde ClientId naast. Nu stopt het met exit 6. Op deze pc staat geen IB Tools in
  ProgramData; hoe de uitrol bij gebruikers is gedaan, is niet nagegaan.
- **Testslot in de Revit-test:** het slot wordt pas genomen vlak voor het deel dat het altijd
  vrijgeeft. Daarvoor kon een fout bij het voorbereiden het slot laten staan.

Na deze wijzigingen zijn de zips opnieuw gemaakt (115 unit-tests geslaagd, 4 builds zonder
waarschuwingen) en zijn beide tests opnieuw gedraaid, ook de test in Revit.

## 3. In Revit 2023 (`tests/Release/revit-check-install.ps1`, met testslot)

De 2023-zip is uitgepakt en gemarkeerd als download. Per scenario is een eigen Revit gestart
(pid vastgelegd) en daarna door het script zelf gesloten. De oorspronkelijke bestanden in
`Addins\2023` stonden achteraf weer terug (zelfde tijdstempels), het slot was vrij en er draaide
geen Revit meer.

- **a. Gedownloade bestanden zo in de Addins-map gezet** (nog geblokkeerd): Revit vraagt
  "Security - Unsigned Add-In" (Always Load gekozen) en meldt daarna **"External Tools - External
  Tool Failure: Revit cannot run the external application "IB Tools""**. Het journal zegt
  `API_ERROR { Starting External Application: IB Tools ... FoundationProjectionToggle.dll,
  Assembly Version: 2.1.0.0 }`. Het tabblad komt er niet.
- **b. Dezelfde bestanden via Installeren.cmd** (vier keer gedraaid, vóór en na elke reviewronde,
  steeds met dezelfde uitkomst): exit 0 met "Geinstalleerd: IB Tools
  dll-versie 2.1.0.0, gedeblokkeerd". Revit start IB Tools: de log meldt "Add-in startup completed
  successfully" en versie 2.1.0. Het journal toont `API_SUCCESS` voor de vijf knoppen (Hide,
  Paalcoördinaten, Worksets, Modelpunt, Remmingwerk) en voor het starten van IB Tools.
  De vraag "Unsigned Add-In" kwam hier niet meer, omdat Revit de keuze Always Load uit scenario a
  onthoudt. Een gebruiker die voor het eerst installeert krijgt die vraag wel.

Dit bevestigt de reden voor het script: zonder deblokkeren werkt een gedownloade release in
Revit 2023 niet.

## Niet getest

- De Revit-harness (64 checks) is niet opnieuw gedraaid: er is geen add-in-code veranderd.
- Revit 2022, 2024 en 2025 zijn niet in Revit zelf getest; ze zijn alleen met het testscript
  zonder Revit gecontroleerd (2025 is op deze pc niet geïnstalleerd).
- Dubbelklikken in de echte Verkenner, met de waarschuwing van Windows of SmartScreen, is niet
  nagebootst. Het script is wel gestart met de opdracht die de Verkenner gebruikt. Wat Windows
  dan vraagt staat in `LEESMIJ.txt`.
- Een pc waarop PowerShell-scripts door beleid zijn geblokkeerd (bijvoorbeeld Constrained
  Language Mode) is niet getest. Daarvoor staat de handmatige route in `LEESMIJ.txt` en in de
  README.
