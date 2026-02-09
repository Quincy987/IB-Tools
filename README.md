# Foundation Projection Toggle

A Revit add-in that lets you toggle the visibility of **Structural Foundation projection lines** with a single button click. No more digging through Visibility/Graphics overrides.

Supports Revit **2022 and newer**.

## Installation

1. Download the latest release from the [Releases](https://github.com/Quincy987/Revit-projectlijnen/releases) page
2. Copy both files to your Revit Addins folder:

   ```
   %APPDATA%\Autodesk\Revit\Addins\<your Revit version>\
   ```

   For example, for Revit 2023:
   ```
   %APPDATA%\Autodesk\Revit\Addins\2023\
   ```

   The two files you need:
   - `FoundationProjectionToggle.dll`
   - `FoundationProjectionToggle.addin`

3. Start Revit. When prompted about the new add-in, click **Always Load**

A new **Foundation** tab will appear in the ribbon with a toggle button.

## Usage

Open any floor plan, ceiling plan, or section view, then click the **Toggle Projection Lines** button in the Foundation tab. The button icon updates to reflect the current state.

The button is automatically disabled in views that don't support projection line overrides (3D views, schedules, sheets, etc.).

## Supported Languages

The add-in detects your Revit language and looks for the correct subcategory name. Supported languages: English, Dutch, German, Spanish, Italian, Portuguese, Chinese, and Japanese.

## Building from Source

Requires the .NET Framework 4.8 SDK.

```powershell
.\build.ps1                      # Builds for Revit 2023 (default)
.\build.ps1 -RevitVersion 2024   # Target a specific version
```

The build script will close Revit, compile the add-in, deploy it to the Addins folder, and restart Revit.
