# IB Tools for Revit

Revit add-in of Ingenieursbureau Gemeente Amsterdam: one ribbon tab **IB Tools** with buttons that
replace the bureau's Dynamo scripts.

Current buttons:

- **Weergave → Hide/Show**: toggles the visibility of **Structural Foundation projection lines**
  in the active view (no digging through Visibility/Graphics overrides).

Builds for Revit **2022, 2023, 2024 and 2025** (tested in Revit 2023).

## Installation

1. Copy both files to your Revit Addins folder:

   ```
   %APPDATA%\Autodesk\Revit\Addins\<your Revit version>\
   ```

   - `FoundationProjectionToggle.dll` (from `bin\Release\Revit<version>\`)
   - `FoundationProjectionToggle.addin`

   Use the dll built for that exact Revit version (2025 is a .NET 8 build).

2. Start Revit. When prompted about the add-in **IB Tools** (Ingenieursbureau Gemeente Amsterdam),
   click **Always Load**.

The **IB Tools** tab appears in the ribbon.

## Usage

Open a floor plan, ceiling plan, section, elevation or 3D view and click **Hide/Show** in the
**Weergave** panel. The button text and icon follow the state of the active view. It is disabled
in views without Visibility/Graphics overrides (schedules, sheets, legends) and when a view
template locks V/G.

The add-in detects the Revit language for the projection subcategory name: English, Dutch,
German, Spanish, Italian, Portuguese, Chinese and Japanese.

## Building from source

Requires the .NET 8 SDK (it also builds the .NET Framework 4.8 targets).

```powershell
.\build.ps1 -NoDeploy                    # compile only (safe, never touches Revit)
.\build.ps1 -NoDeploy -RevitVersion 2025
.\build.ps1                              # build + deploy + start Revit 2023 (uses the test lock)
```

`build.ps1` never closes a Revit it did not start itself. Deploying and testing in Revit go one
session at a time via a lock file; see [docs/TESTING.md](docs/TESTING.md).

| Revit | Target framework | Status |
|---|---|---|
| 2022, 2023, 2024 | net48 | builds; 2023 tested in Revit |
| 2025 | net8.0-windows | builds; not run (Revit 2025 not installed here) |
| 2026 | net8.0-windows | not enabled yet (add `2026` to `build.ps1` ValidateSet; SDK package 2026 exists; `ElementId.IntegerValue` is removed in 2026) |

## Adding a button (for developers)

- Ribbon: one line in `UI/RibbonManager.cs` → `CreateRibbon`:
  `AddPushButton<MyCommand>(application, "Panel", "InternalName", "Label", "Tooltip", iconName: "my-icon", availability: typeof(ProjectDocumentAvailability));`
  Panels are created on first use; icons are `UI/Resources/icons/<iconName>-32.png` / `-16.png`.
- Availability: `ProjectDocumentAvailability` (project open, not in the family editor) or `ViewBasedAvailability`.
- Input windows: derive from `UI/RevitWindow` (owned by Revit, Esc closes, `ShowModal()`), XAML root `<ui:RevitWindow>`.
- Results: `ResultDialog.Show(uiapp, title, summary, details)` with selectable text and *Kopiëren naar klembord*.
- Conditional compilation: `REVIT2023`, `REVIT2024`, `REVIT2025`, `REVIT2025_OR_GREATER`.

## Renaming the dll / manifest (separate step, not done yet)

The assembly and manifest are still called `FoundationProjectionToggle` and the manifest keeps its
`ClientId`. Renaming them (e.g. to `IBTools.dll` / `IBTools.addin`) must be one deliberate step:
every install must delete the old `FoundationProjectionToggle.addin`, otherwise Revit finds two
manifests and loads the add-in twice (or rejects the duplicate ClientId). Also update the logger
folder, `build.ps1`, the csproj `AssemblyName` and the resource prefix in `IconLoader`.
