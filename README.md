# Shutter

A Windows screenshot and annotation tool for personal use, inspired by Shottr. Built with C# / WPF (.NET 8).

**Capture:** area, fullscreen, delayed, repeat last area, active window, any window (hover and click), scrolling capture, load from clipboard.
**Editor:** arrow, line, rectangle, oval, text, freehand, highlighter, counters, blur/erase, spotlight, ruler, magnifier, color picker, crop/uncrop/resize, undo/redo.
**Extras:** backdrops (gradient/solid/image/wallpaper), OCR and QR reading (Windows OCR + ZXing), pin to screen, screenshot library with text search, preview card, tray menu, preferences.

## Build

Requires the .NET 8 SDK (and [Inno Setup 6](https://jrsoftware.org/isinfo.php) for the installer).

```powershell
dotnet build Shutter\Shutter.csproj -c Release      # quick dev build
.\build.ps1                                          # self-contained exe + installer
```

The installer is written to `installer\output\`.

## Layout

| Path | Contents |
| --- | --- |
| `Shutter/` | The WPF app |
| `Shutter/Resources/` | Theme tokens, icon geometries, control styles |
| `installer/Shutter.iss` | Inno Setup script (per-user install, no admin) |
| `Shutter Redesign/` | UI mockups, icons and brand assets the UI was built from |
| `docs/` | Notes on the original app's structure |

Settings and logs live in `%AppData%\Shutter`.
