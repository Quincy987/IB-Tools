# Project Instructions

## IB Tools (Revit add-in)

This add-in is **IB Tools**: one ribbon tab "IB Tools" with buttons that replace Dynamo scripts of
Ingenieursbureau Gemeente Amsterdam. Assembly/manifest are still named `FoundationProjectionToggle`
(renaming is a separate step, see README). Toolkit for new buttons:

- Ribbon: one line in `RibbonManager.CreateRibbon`:
  `RibbonManager.AddPushButton<MyCommand>(application, "Panel", "InternalName", "Label", "Tooltip", iconName: "my-icon", availability: typeof(ProjectDocumentAvailability));`
  Icons: `UI/Resources/icons/<iconName>-32.png` and `-16.png` (embedded automatically).
- Availability: `ProjectDocumentAvailability` (project open, not family editor), `ViewBasedAvailability` (V/G-capable view).
- Windows: derive from `UI/RevitWindow` (owned by Revit, Esc closes, `ShowModal()`); XAML root `<ui:RevitWindow ...>`.
- Results: `ResultDialog.Show(commandData.Application, title, summary, details)` (selectable text + "Kopiëren naar klembord"); `ClipboardHelper.TrySetText/TryGetText`.

### Build & Deploy
```powershell
.\build.ps1 -NoDeploy                    # compile only - always allowed, never touches Revit
.\build.ps1 -NoDeploy -RevitVersion 2025 # 2022/2023/2024 = net48, 2025 = net8.0-windows
.\build.ps1                              # lock + build + deploy + start Revit 2023 (own pid recorded)
.\build.ps1 -Debug                       # same, with debug logging
```
Never close, kill or restart a Revit you did not start: other sessions run their own Revit on this machine.
Never ask the user to manually close/restart Revit.

### Testing in Revit: one session at a time (docs/TESTING.md)
- Lock `%LOCALAPPDATA%\IBTools\revit-test.lock` (`<branch or thread name> <UTC time>`); check with `.\scripts\revit-lock.ps1 status`.
- Held by someone else and younger than 2 h: do NOT deploy or start Revit; schedule a self-wake in 10 min and retry.
- Otherwise `build.ps1` takes it; test; then ALWAYS (also on errors) `.\scripts\revit-lock.ps1 release` (closes only your Revit pid, removes the lock).
- Set `$env:IBTOOLS_LOCK_OWNER` to your thread name so the owner stays the same across turns.

### Diagnostic Dialogs
When creating diagnostic/debug dialogs in Revit:
- **Always include a "Copy to Clipboard" feature** - users cannot select text in TaskDialog
- Use `ResultDialog.Show(..., copyImmediately: true)` or `ClipboardHelper.TrySetText()`
- Show a "copied to clipboard" confirmation in the dialog

### Debug Logging
- Logs are in: `%LOCALAPPDATA%\FoundationProjectionToggle\logs\`
- Enable with environment variable: `PROJECTION_TOGGLE_DEBUG=1`
- The `-Debug` flag on build.ps1 enables this automatically
