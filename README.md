# Shutter

A Windows screenshot and annotation tool for personal use, inspired by Shottr. Built with C# / WPF (.NET 8).

**Capture:** area, fullscreen, delayed, repeat last area, active window, any window (hover and click), scrolling capture, load from clipboard.
**Editor:** arrow, line, rectangle, oval, text, freehand, highlighter, counters, blur/erase, spotlight, ruler, magnifier, color picker, crop/uncrop/resize, undo/redo.
**Extras:** backdrops (gradient/solid/image/wallpaper), OCR and QR reading (Windows OCR + ZXing), pin to screen, screenshot library with text search, preview card, tray menu, preferences.

## Install

1. Go to the [**Releases** page](https://github.com/Nanthia/Shutter/releases/latest) of this repo (sign in to GitHub first if the repo is private).
2. Under **Assets**, download **`Shutter-Setup-<version>.exe`**, for example `Shutter-Setup-1.0.4.exe`. Ignore the "Source code" downloads; those are only for developers.
3. Run the downloaded file. Windows may show a blue **"Windows protected your PC"** screen because the installer is not code-signed. Click **More info**, then **Run anyway**.
4. Follow the wizard. Choose whether to add a desktop shortcut and whether Shutter should start when you sign in.
5. Shutter runs in the system tray (near the clock; check the hidden-icons arrow if you don't see it). Click the tray icon, or press **Ctrl+Shift+4**, to capture an area.

Notes:

- It installs for your user only into `%LocalAppData%\Programs\Shutter`, so no admin rights are needed.
- Everything it needs, including the .NET runtime, is bundled. You do not need to install anything else.
- Requires 64-bit Windows 10 (version 2004 or newer) or Windows 11.
- To update, run a newer installer over the old one; it closes the running app and keeps your settings.
- To uninstall, use **Settings > Apps > Installed apps > Shutter**. Your saved screenshots are never deleted.
- Settings and logs are in `%AppData%\Shutter`. To move to a new PC, copy that folder and your screenshots folder (default `Pictures\Shutter`).

## Default hotkeys

| Action | Hotkey |
| --- | --- |
| Area capture | Ctrl+Shift+4 |
| Scrolling capture | Ctrl+Shift+1 |
| Fullscreen | Ctrl+Shift+3 |
| Delayed (3s) | Ctrl+Shift+5 |
| Repeat last area | Ctrl+Shift+6 |
| Active window | Ctrl+Shift+7 |
| Any window | Ctrl+Shift+9 |
| Load from clipboard | Ctrl+Shift+8 |
| Recognize text / QR | Ctrl+Shift+2 |
| Library | Ctrl+Shift+0 |

All hotkeys can be changed in **Preferences > Hotkeys**.

## Build

Requires the .NET 8 SDK (and [Inno Setup 6](https://jrsoftware.org/isinfo.php) for the installer).

```powershell
dotnet build Shutter\Shutter.csproj -c Release      # quick dev build
.\build.ps1                                          # self-contained exe + installer
```

The installer is written to `installer\output\`.

To rebuild on a fresh PC you need Git, the .NET 8 SDK and Inno Setup 6:

```powershell
winget install Git.Git Microsoft.DotNet.SDK.8 JRSoftware.InnoSetup
git clone https://github.com/Nanthia/Shutter.git
cd Shutter
.\build.ps1
```

Publishing a release (after bumping the version in `Shutter\Shutter.csproj` and `installer\Shutter.iss`):

```powershell
gh release create v1.0.5 installer\output\Shutter-Setup-1.0.5.exe --title "Shutter 1.0.5" --notes "What changed"
```

## Layout

| Path | Contents |
| --- | --- |
| `Shutter/` | The WPF app |
| `Shutter/Resources/` | Theme tokens, icon geometries, control styles |
| `installer/Shutter.iss` | Inno Setup script (per-user install, no admin) |
| `Shutter Redesign/` | UI mockups, icons and brand assets the UI was built from |
| `docs/` | Notes on the original app's structure |

Settings and logs live in `%AppData%\Shutter`.
