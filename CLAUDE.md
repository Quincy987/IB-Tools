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

---

## AI-Powered Revit Diagnostics (VibeModel)

The AI integration for Revit has been moved to its own project: **VibeModel**

- **Location**: `C:\Development\VibeModel`
- **How it works**: Embedded HTTP server inside Revit on `localhost:18884`
- **Usage**: `curl -s http://localhost:18884/help`

See `C:\Development\VibeModel\CLAUDE.md` for full documentation.

---

## Task Management with Ralphy

This project uses `ralphy` CLI for autonomous AI coding loops. Ralphy orchestrates AI agents (including Claude Code) to complete coding tasks.

### Usage

**Single Task Mode** - Execute a one-off instruction:
```
ralphy "add login button"
```

**Task List Mode** - Process multiple items from PRD.md:
```
ralphy --prd PRD.md
```

Or simply run `ralphy` (looks for PRD.md in current directory by default).

### PRD.md Format

Tasks are defined using markdown checkboxes:

```markdown
## Tasks
- [ ] create auth
- [ ] add dashboard
- [x] done task (skipped)
```

### Key Flags

- `--prd PATH`: Specify task source file
- `--fast`: Skip testing and linting
- `--max-retries N`: Set retry attempts
- `--dry-run`: Preview without execution
- `--parallel`: Run multiple agents simultaneously
- `--branch-per-task`: Create branches per task
- `--create-pr`: Generate pull requests

### Initialize Project Config

```
ralphy --init
```

This generates `.ralphy/config.yaml` with project settings, commands, and execution rules.

### More Info

https://github.com/michaelshimeles/ralphy
