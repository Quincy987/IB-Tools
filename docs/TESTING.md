# Testen in Revit: om de beurt

Op deze pc draaien meerdere sessies (IB Tools, VibeModel, ...), elk soms met een eigen Revit.
Er is één Addins-map per Revit-versie, dus deployen en Revit-testen gaat **om de beurt**.

## Afspraak

**Compileren mag altijd, zonder slot:**

```powershell
.\build.ps1 -NoDeploy                    # Revit 2023
.\build.ps1 -NoDeploy -RevitVersion 2025
```

In CI en in git-worktrees (chain/build-checks) is `-NoDeploy` de standaard; `-Deploy` forceert deployen.

**Deployen en Revit starten alleen met het slot:**

- Slot: `%LOCALAPPDATA%\IBTools\revit-test.lock`, één regel `<branch of threadnaam> <UTC-tijd>`.
- Bestaat het slot, is het van iemand anders en jonger dan 2 uur: **niet** testen. Kom over ~10 minuten terug (self-wake 10 min).
- Anders: slot nemen, testen, je eigen Revit sluiten en het slot weghalen, **ook bij fouten** (`finally`).

```powershell
.\scripts\revit-lock.ps1 status     # vrij / bezet door wie, sinds wanneer (exit 2 = bezet)
.\build.ps1                         # neemt het slot, bouwt, deployt, start Revit (pid vastgelegd)
#   ... testen ...
.\scripts\revit-lock.ps1 release    # sluit ALLEEN jouw Revit (pid) en geeft het slot vrij
```

`build.ps1` neemt het slot zelf (exit 2 als het bezet is) en geeft het terug als de build mislukt.
Na een geslaagde deploy blijft het slot van jou tot `release`.

Eigenaar = `$env:IBTOOLS_LOCK_OWNER`, anders `<git-branch>@<repomap>`. Zet in een bot-thread bij
voorkeur `$env:IBTOOLS_LOCK_OWNER = "<threadnaam>"`, zodat `release` in een volgende beurt dezelfde eigenaar is.

## Wat de scripts wel en niet doen

- Ze sluiten alleen de Revit die `build.ps1` zelf startte, voor dezelfde eigenaar
  (`%LOCALAPPDATA%\IBTools\revit.pid`: pid + starttijd, dus geen verwisseling bij hergebruikt pid).
- Een **andere** Revit die de dll vasthoudt wordt nooit afgesloten. De oude dll wordt dan opzij
  gezet (`*.dll.old-<tijd>`) en de nieuwe ernaast gekopieerd; die andere Revit werkt door met de
  oude versie en laadt de nieuwe pas bij zijn volgende start. Lukt ook dat niet: exit 3, niets veranderd.
- Let op: elke Revit 2023 op deze pc laadt IB Tools uit dezelfde Addins-map. Een half-af build
  die je deployt, komt dus ook in andere sessies terecht zodra die Revit herstarten.

## Exitcodes `build.ps1`

`0` ok · `1` build mislukt · `2` testslot bezet · `3` deploy geblokkeerd
