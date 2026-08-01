# AEP Downgrader

A small Windows desktop app (WPF, .NET 8) that converts Adobe After Effects
project files (`.aep`) from a newer version down to an older one, so you can
open a project in a copy of After Effects that doesn't yet support the file's
original version.

It works by patching the version byte in the project's binary header — it
does **not** re-encode effects, expressions, or plugin data introduced in the
newer version, so results can vary depending on what the project actually
uses.

![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4)
![WPF](https://img.shields.io/badge/UI-WPF-0078D4)
![License: MIT](https://img.shields.io/badge/license-MIT-green)

## Features

- **Drag & drop or browse** for one or more `.aep` files at once
- **Automatic version detection** for every selected file
- **Batch conversion** to any valid target version older than the oldest
  detected input version
- Clear warnings for **experimental target versions** and files with an
  **unknown/unsupported source version**
- Converted files are written next to the originals as
  `<name>_AE<version>x.aep`, so nothing is overwritten
- Built-in **debug mode** with a log viewer, exportable debug reports, and a
  system-information snapshot, for troubleshooting failed conversions
- **Update checks** against GitHub releases, with "skip this version" support

## Screenshots

The app uses a dark theme with a custom title bar, a two-panel layout
(quick actions on the left, conversion workspace on the right), and an
activity log with a live progress bar.

## Getting started

### Requirements

- Windows 10 or later
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (for building
  from source) — an already-built release only needs the .NET 8 Desktop
  Runtime

### Build and run

```bash
git clone https://github.com/itsAnchorpoint/AEP-Downgrader.git
cd AEP-Downgrader
dotnet build
dotnet run --project AEPDowngrader
```

Or open `AEPDowngrader.sln` in Visual Studio 2022+ and press **F5**.

### Publish a self-contained build

```bash
dotnet publish AEPDowngrader/AEPDowngrader.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

The output `.exe` will be under
`AEPDowngrader/bin/Release/net8.0-windows/win-x64/publish/`.

## Usage

1. Click **Select Files...** (or drag `.aep` files onto the drop zone).
2. The app detects each file's source AE version and shows the versions
   found.
3. Choose a **Target Version** from the dropdown — only versions older than
   the oldest detected file are offered. Versions flagged **EXPERIMENTAL**
   aren't guaranteed to be compatible.
4. Click **Convert**. Progress and per-file results appear in the activity
   log; converted files are saved next to their originals.
5. Use **Open Output Folder** to jump straight to the converted files.

If you run into a conversion that doesn't work as expected, enable
**Debug → Enable Debug Mode**, reproduce the issue, then use
**Debug → View Debug Logs** or **Export Debug Report** when filing an issue.

## Project layout

```
AEPDowngrader/
├─ App.xaml(.cs)                 Application entry point, version constants
├─ MainWindow.xaml(.cs)           Main window UI and workflow orchestration
├─ Controls/InputDropZone.xaml    Drag-and-drop file picker control
├─ Converters/                    XAML value converters
├─ Models/TargetVersionOption.cs  Target-version combo box item
├─ Services/
│  ├─ AepConverter.cs             Core .aep header detection/patching logic
│  ├─ AppSettings.cs              Persisted user settings (last folders, etc.)
│  ├─ DebugLogger.cs              Debug logging, system/memory info
│  └─ UpdateChecker.cs            GitHub releases update check
├─ Themes/DarkTheme.xaml          App-wide dark theme (colors, control styles)
└─ Views/                         Debug log viewer & update-available dialogs
```

## Disclaimer

This tool patches binary project files. Always keep a backup of your
original `.aep` files — conversion never overwrites the source file, but as
with any binary patching tool, compatibility with your specific project
contents isn't guaranteed, especially for versions marked experimental.

## License

MIT — see [LICENSE](LICENSE).
