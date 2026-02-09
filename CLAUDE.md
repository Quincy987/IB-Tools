# Project Instructions

## Revit Add-in Development Workflow

This is a Revit add-in project for toggling Structural Foundation projection line visibility. Follow these rules when making changes:

### Build & Deploy
**Always use the build script** to close Revit, build, and restart automatically:
```powershell
.\build.ps1                      # Default: Revit 2023
.\build.ps1 -RevitVersion 2024   # Target specific version
.\build.ps1 -Debug               # Enable debug logging
```

Or via command prompt:
```cmd
build.cmd
build.cmd 2024
build.cmd --debug
```

**IMPORTANT**: Never ask the user to manually close/restart Revit. Always run the build script.

### Diagnostic Dialogs
When creating diagnostic/debug dialogs in Revit:
- **Always include a "Copy to Clipboard" feature** - users cannot select text in TaskDialog
- Use `System.Windows.Clipboard.SetText()` to copy output automatically
- Show "TEXT COPIED TO CLIPBOARD" confirmation in the dialog

### Debug Logging
- Logs are in: `%LOCALAPPDATA%\FoundationProjectionToggle\logs\`
- Enable with environment variable: `PROJECTION_TOGGLE_DEBUG=1`
- The `-Debug` flag on build.ps1 enables this automatically

