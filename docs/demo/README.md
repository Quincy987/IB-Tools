# Demo-video IB Tools

`ibtools-demo.mp4` (±2 min, 1600×1000, 10 fps) laat de drie knoppen zien op een kopie van het
kademuur-model Prinsengracht (RD-coördinaten, 40 houten palen): Worksets → Modelpunt op coördinaat →
Paalcoördinaten, met titelkaarten en ondertitels. `stills/` zijn de beginbeelden van elke scène.

## Opnieuw opnemen

```powershell
.\scripts\demo\run-demo.ps1            # neemt het Revit-testslot, bouwt, deployt, neemt op, snijdt
.\scripts\demo\run-demo.ps1 -CutOnly   # alleen opnieuw snijden (kaarten/ondertitels) uit de laatste take
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
- Scènes, teksten en wachttijden staan in `drive.py` (`mark("card"/"caption", ...)`).
