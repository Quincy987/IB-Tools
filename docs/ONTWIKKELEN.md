# Ontwikkelen: een tool toevoegen

IB Tools is één add-in (één dll, één tabblad). Elke tool staat in een eigen map
`src/IBTools/Tools/<Tool>/`; wat meerdere tools gebruiken staat in `src/IBTools/Shared/`.

## Stappen

1. **Map** `src/IBTools/Tools/<Tool>/` (naam zonder trema of spaties), namespace
   `IBTools.Tools.<Tool>`. Revit-vrije rekencode in `Core/` (namespace `IBTools.Tools.<Tool>.Core`),
   zodat hij unit-tests kan krijgen zonder Revit.
2. **Command**: een `IExternalCommand` met `[Transaction(TransactionMode.Manual)]`. Doe alle
   wijzigingen in één transactie, zodat één Ctrl+Z alles ongedaan maakt.
3. **Lint**: één regel in `App.OnStartup`, op de plek waar de knop in de volgorde hoort (de volgorde
   van de regels is de volgorde van panelen en knoppen):

   ```csharp
   RibbonManager.AddPushButton<MyCommand>(application, "Paneel", "InternalName", "Label", "Tooltip",
       iconName: "my-icon", availability: typeof(ProjectDocumentAvailability));
   ```

   `InternalName` is het vaste id van de knop (Revit gebruikt het voor sneltoetsen; tests en
   demo-scripts drukken knoppen in via `CustomCtrl_%CustomCtrl_%IB Tools%<Paneel>%<InternalName>`).
   Verander het niet meer na een release.
4. **Icoon**: `Tools/<Tool>/Resources/icons/<my-icon>-32.png` en `-16.png`. Ze worden automatisch
   ingebed als `IBTools.icons.<bestand>`; de bestandsnaam moet dus uniek zijn over alle tools
   (`EmbeddedResourceTests` controleert dat).
5. **Families** die de tool zelf meelevert: `Tools/<Tool>/Resources/families/<Familie>.rfa`, ingebed als
   `IBTools.families.<bestand>`, laden met `EmbeddedFamilyLoader` (nooit van het netwerk). Zet de
   familie ook in de `InlineData` van `Embedded_family_exists_in_its_tool` (EmbeddedResourceTests).
6. **README.md** in de toolmap, Nederlands, met de kopjes: Wat doet het / Vervangt / Wat heb je
   nodig / Zo werkt het / Wat krijg je / Verschillen met Dynamo / Beperkingen / Demo en test. Zet de
   tool in de tabel in de hoofd-README en in de toollijst van `scripts/release/LEESMIJ.template.txt`
   (die gaat in elke release-zip).
7. **Tests**: unit-tests in `tests/IBTools.Tests/<Tool>/` (het testproject linkt `Tools/*/Core/**`
   automatisch; andere bestanden expliciet in `IBTools.Tests.csproj`). Een Revit-test als job in
   `tests/RevitHarness/<Tool>/`; laat `HarnessApp.OnStartup` hem kiezen aan een markeerbestand in
   de jobmap, zoals `remmingwerk.txt` voor `Remmingwerk/RemmingwerkHarness.cs`. Testverslag in
   `Tools/<Tool>/verify/`.

## Bouwstenen in Shared/

- `ProjectDocumentAvailability`: knop alleen actief met een project open (niet in de familie-editor).
  `ViewBasedAvailability` (alleen in views met V/G) hoort bij Projectielijnen.
- Vensters: leid af van `RevitWindow` (eigenaar Revit, Esc sluit, `ShowModal()`), XAML-root
  `<ui:RevitWindow ...>` met `xmlns:ui="clr-namespace:IBTools.Shared"`.
- Resultaten: `ResultDialog.Show(uiapp, titel, samenvatting, details)` met selecteerbare tekst en
  *Kopiëren naar klembord*; `ClipboardHelper.TrySetText/TryGetText`. Diagnose- en foutmeldingen
  altijd met kopiëren naar klembord (`copyImmediately: true`).
- `Logger.Info/Warn/Error`: schrijft alleen met `IBTOOLS_DEBUG=1`, naar `%LOCALAPPDATA%\IBTools\logs\`.
- `CoordinateInputParser`: getallen en RD-coördinaten uit tekst (komma of punt, m of mm).

## Taal en versies

- Revit 2022–2024 bouwen als net48, dus C# 7.3. Het testproject staat op `LangVersion 7.3` om dat
  te bewaken; gebruik geen nieuwere taalfeatures in de add-in.
- Voorwaardelijke compilatie: `REVIT2022` … `REVIT2025`, `REVIT2025_OR_GREATER` (.NET 8).

## Bouwen, testen, release

- `dotnet build` / `dotnet test` in de hoofdmap; `.\build.ps1 -NoDeploy [-RevitVersion 2025]`.
- Testen in Revit om de beurt met het slot: [TESTING.md](TESTING.md).
- `.\scripts\release.ps1`: tests en per Revit-versie een zip in
  `%LOCALAPPDATA%\IBTools\release\v<versie>\` met precies vier bestanden: de dll, het manifest,
  `Installeren.cmd` en `LEESMIJ.txt`. Het publiceert niets; een GitHub-release maak je daarna zelf
  (versie staat in `src/IBTools/IBTools.csproj`).
- `Installeren.cmd` en `LEESMIJ.txt` komen uit `scripts/release/*.template.*`; `{{REVIT}}`,
  `{{VERSION}}`, `{{DLL}}` en `{{ADDIN}}` vult `scripts/release/ReleaseFiles.ps1` in (gewoon ASCII,
  CRLF; dat bestand houdt ook de namen van de geleverde bestanden bij). Het
  installatiescript is een cmd-kop die de PowerShell onder de regel `##PS-START` uitvoert; die
  markering mag maar één keer in het bestand staan. Het kopieert naar
  `%APPDATA%\Autodesk\Revit\Addins\<jaar>\` en haalt de Windows-downloadblokkade
  (`Zone.Identifier`) eraf: zonder dat weigert Revit 2023 de dll ("External Tool Failure").
  Exitcodes: 0 klaar, 2 zip niet uitgepakt, 3 Revit-versie niet gevonden, 4 dll in gebruik (Revit
  draait), 5 mislukt (wat al was omgewisseld is teruggedraaid).
- Na een wijziging aan het installatiescript: `.\scripts\release.ps1`, dan
  `.\tests\Release\test-install.ps1` (zonder Revit, eigen APPDATA in %TEMP%: alle jaren, bijwerken,
  Revit ontbreekt, dll in gebruik, fout halverwege, zip niet uitgepakt) en met het testslot
  `.\tests\Release\revit-check-install.ps1` (echte Revit 2023: geblokkeerde bestanden tegenover
  Installeren.cmd; zet daarna de oorspronkelijke bestanden in de Addins-map terug).
