# Shutter

A Windows screenshot and annotation tool for personal use, inspired by Shottr.

**Capture:** area, fullscreen, delayed, repeat last area, active window, any window (hover and click), scrolling capture, load from clipboard.
**Editor:** arrow, line, rectangle, oval, text, freehand, highlighter, counters, blur/erase, spotlight, ruler, magnifier, color picker, crop/uncrop/resize, undo/redo.
**Extras:** backdrops (gradient/solid/image/wallpaper), OCR and QR reading, pin to screen, screenshot library with text search, preview card, tray menu, preferences.

---

## Download and install (no building needed)

**You do not need to build anything.** Just download the installer and run it.

1. Open the **[Releases page](https://github.com/Nanthia/Shutter/releases/latest)** (sign in to GitHub first if the repo is private).
2. Under **Assets**, click **`Shutter-Setup-<version>.exe`** (for example `Shutter-Setup-1.0.4.exe`) to download it.
   Ignore the "Source code (zip)" and "Source code (tar.gz)" links; those are for developers.
3. Run the downloaded file. If Windows shows a blue **"Windows protected your PC"** screen (the installer isn't code-signed), click **More info**, then **Run anyway**.
4. Follow the wizard. You can choose a desktop shortcut and whether Shutter starts when you sign in.
5. Shutter runs in the system tray, near the clock (check the hidden-icons arrow if you don't see it). Click the tray icon, or press **Ctrl+Shift+4**, to capture an area.

That's it. The installer includes everything Shutter needs, including the .NET runtime, so there is nothing else to download or install.

**Requirements:** 64-bit Windows 10 (version 2004 or newer) or Windows 11. No admin rights needed.

### Updating, uninstalling, moving to a new PC

- **Update:** run a newer installer over the old one. It closes the running app and keeps your settings.
- **Uninstall:** Windows **Settings > Apps > Installed apps > Shutter**. Your saved screenshots are never deleted.
- **New PC:** install the same way. To bring your settings across, copy `%AppData%\Shutter` (and your screenshots folder, `Pictures\Shutter` by default) from the old PC.

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

All hotkeys can be changed in **Preferences > Hotkeys**. Settings and logs are stored in `%AppData%\Shutter`.

---

## For developers only: building from source

Everyone else can skip this section. It is only needed if you want to change the code and make your own installer.

You need Git, the .NET 8 SDK and [Inno Setup 6](https://jrsoftware.org/isinfo.php):

```powershell
winget install Git.Git Microsoft.DotNet.SDK.8 JRSoftware.InnoSetup
git clone https://github.com/Nanthia/Shutter.git
cd Shutter
.\build.ps1                                          # self-contained exe + installer in installer\output\
dotnet build Shutter\Shutter.csproj -c Release      # or just a quick dev build
```

To publish a new release, bump the version in `Shutter\Shutter.csproj` and `installer\Shutter.iss`, run `.\build.ps1`, then:

```powershell
gh release create v1.0.5 installer\output\Shutter-Setup-1.0.5.exe --title "Shutter 1.0.5" --notes "What changed"
```

### Repository layout

| Path | Contents |
| --- | --- |
| `Shutter/` | The WPF / .NET 8 app |
| `Shutter/Resources/` | Theme tokens, icon geometries, control styles |
| `installer/Shutter.iss` | Inno Setup script (per-user install, no admin) |
| `build.ps1` | Builds the app and the installer |
| `Shutter Redesign/` | UI mockups, icons and brand assets the UI was built from |
| `docs/` | Notes on the original app's structure |
