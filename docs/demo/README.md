# Demo-video's IB Tools

- `ibtools-demo.mp4` (±2 min, 1600×1000, 10 fps): de knoppen Worksets → Modelpunt op coördinaat →
  Paalcoördinaten op een kopie van het kademuur-model Prinsengracht (RD-coördinaten, 40 houten palen),
  met titelkaarten en ondertitels. `stills/` zijn de beginbeelden van elke scène.
- `remmingwerk-demo.mp4` (±75 s): de knop Remmingwerk, apart: modellijn vóór de kade, venster,
  Plaatsen, resultaat, 3D, Ctrl+Z/Ctrl+Y. `stills-remmingwerk/`.
- Hide/Show (projectielijnen) staat bewust niet in een video: die knop is al bij gebruikers uitgerold.

## Opnieuw opnemen

```powershell
.\scripts\demo\run-demo.ps1                      # ibtools-demo.mp4: neemt het Revit-testslot, bouwt, deployt, neemt op, snijdt
.\scripts\demo\run-demo.ps1 -Scene remmingwerk   # remmingwerk-demo.mp4 (eigen take-map, eigen stills)
.\scripts\demo\run-demo.ps1 -CutOnly [-Scene ..] # alleen opnieuw snijden (kaarten/ondertitels) uit de laatste take
```

- `run-demo.ps1` kopieert het bronmodel (`-Source`, standaard
  `C:\Development\VibeModel\demo_out\video\kade\Prinsengracht kademuur 2.0002.rvt`) naar
  `%LOCALAPPDATA%\IBTools\demo\`; de kopie wordt elke keer vers aangemaakt en nooit opgeslagen.
- `drive.py` beantwoordt de startdialogen (Load Always, Unresolved References, foutmelding van een
  andere add-in), verbergt het VibeModel-chatpaneel, geeft de palen de IB-parameters
  X-/Y-coördinaat_funderingspaal (zoals de IB-template), opent de 3D-view "Kade", en drukt dan de
  knoppen op het lint echt in (UI Automation + bewegende muis). Modelzaken gaan via VibeModel in
  dezelfde Revit (`exec`, `activate`; sessiesleutel uit `%LOCALAPPDATA%\VibeModel\servers\<port>.json`).
- `rec.py` neemt het scherm op (±8 fps, jpg-frames); `cut.py` maakt er de mp4 van met kaarten,
  stapbadge en ondertitelbalk (`timeline.json` uit `drive.py`).
- Vereist: Python 3.12 met `pywinauto`, `pywin32`, `mss`, `av`, `imageio-ffmpeg`, `Pillow`.
- Scènes, teksten en wachttijden staan in `drive.py` (`mark("card"/"caption", ...)`); `SCENES` koppelt
  de naam uit `-Scene` aan een functie. Let op: na een selectie springt Revit naar het tabblad Modify,
  daarom klikt `press()` eerst het tabblad IB Tools weer aan.
