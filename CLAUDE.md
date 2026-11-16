# Project Instructions

## IB Tools (Revit add-in)

This repo is **IB Tools**: a library of tools that replace Dynamo scripts of Ingenieursbureau
Gemeente Amsterdam, shipped as ONE add-in with one ribbon tab "IB Tools". Code is `src/IBTools/`:
`App.cs` registers every tool, `Shared/` holds what several tools use, `Tools/<Tool>/` holds one
tool each (code, `Core/` Revit-free maths, `Resources/icons` + `Resources/families`, `README.md`,
`verify/`, `demo/`). Namespaces `IBTools`, `IBTools.Shared`, `IBTools.Tools.<Tool>(.Core)`.
The shipped files are still named `FoundationProjectionToggle.dll/.addin` with the same ClientId,
on purpose (see README "Hernoemen"); do not rename them without an installer. How to add a tool:
`docs/ONTWIKKELEN.md`. Toolkit:

- Ribbon: one line in `App.OnStartup` (the order of the lines is the order on the tab):
  `RibbonManager.AddPushButton<MyCommand>(application, "Panel", "InternalName", "Label", "Tooltip", iconName: "my-icon", availability: typeof(ProjectDocumentAvailability));`
  Icons: `Tools/<Tool>/Resources/icons/<iconName>-32.png` and `-16.png`, embedded as
  `IBTools.icons.<file>` (file names unique across tools). Never change a released InternalName.
- Availability: `ProjectDocumentAvailability` (Shared: project open, not family editor),
  `ViewBasedAvailability` (Projectielijnen: V/G-capable view).
- Windows: derive from `Shared/RevitWindow` (owned by Revit, Esc closes, `ShowModal()`); XAML root
  `<ui:RevitWindow ...>` with `xmlns:ui="clr-namespace:IBTools.Shared"`.
- Results: `ResultDialog.Show(commandData.Application, title, summary, details)` (selectable text + "Kopiëren naar klembord"); `ClipboardHelper.TrySetText/TryGetText`.
- Families shipped with a tool: `Tools/<Tool>/Resources/families/*.rfa`, loaded via `EmbeddedFamilyLoader`.
- Add-in code must stay C# 7.3 (net48 builds); `tests/IBTools.Tests` compiles on 7.3 as a guard.

### Build & Deploy
```powershell
dotnet build; dotnet test                # whole solution + unit tests (no Revit)
.\build.ps1 -NoDeploy                    # compile only - always allowed, never touches Revit
.\build.ps1 -NoDeploy -RevitVersion 2025 # 2022/2023/2024 = net48, 2025 = net8.0-windows
.\build.ps1                              # lock + build + deploy + start Revit 2023 (own pid recorded)
.\build.ps1 -Debug                       # same, with debug logging
.\scripts\release.ps1                    # tests + release zips for 2022-2025 (publishes nothing)
```
Never close, kill or restart a Revit you did not start: other sessions run their own Revit on this machine.
Never ask the user to manually close/restart Revit.

### Testing in Revit: one session at a time (docs/TESTING.md)
- Lock `%LOCALAPPDATA%\IBTools\revit-test.lock` (`<branch or thread name> <UTC time>`); check with `.\scripts\revit-lock.ps1 status`.
- Held by someone else and younger than 2 h: do NOT deploy or start Revit; schedule a self-wake in 10 min and retry.
- Otherwise `build.ps1` takes it; test; then ALWAYS (also on errors) `.\scripts\revit-lock.ps1 release` (closes only your Revit pid, removes the lock).
- Set `$env:IBTOOLS_LOCK_OWNER` to your thread name so the owner stays the same across turns.
- Revit harness jobs per tool in `tests/RevitHarness/<Tool>/` (e.g. `Remmingwerk\run-remmingwerk.ps1`, unattended).

### Diagnostic Dialogs
When creating diagnostic/debug dialogs in Revit:
- **Always include a "Copy to Clipboard" feature** - users cannot select text in TaskDialog
- Use `ResultDialog.Show(..., copyImmediately: true)` or `ClipboardHelper.TrySetText()`
- Show a "copied to clipboard" confirmation in the dialog

### Debug Logging
- Logs are in: `%LOCALAPPDATA%\IBTools\logs\`
- Enable with environment variable: `IBTOOLS_DEBUG=1` (the old `PROJECTION_TOGGLE_DEBUG=1` still works)
- The `-Debug` flag on build.ps1 enables this automatically
