# Shottr 1.9.3 (macOS) -> Windows clone: core spec

Source: `Shottr-1.9.3b.dmg`, sha256 `9debebb9...5820de`. Main binary is a Swift/AppKit Mach-O
(universal, analyzed x86_64 slice), 5.9k functions decompiled to `decomp/x86_64/decompiled.c`.
Swift method names are stripped (`FUN_xxxx`); ivars, ObjC selectors, class names and strings survive.
Everything below is OBSERVED from class/ivar/string evidence unless marked (inferred).

## Architecture observed
- `AppLogic` (central state): imgSource, pixels/bytesPerRow, currentSelectionMode, pins, lastOCROriginal,
  screenshotDate, folderWithAccessURL, saveAttempts, ocrBusy, color value/format strings.
- `Capturer`, `ScrollCapturer`, `VisionStitcher` (scrolling capture), `RegionSelector` (area picker window).
- `MainWindowController` + `ViewController` + `ShotView` (editor), `PreviewWindowController` (small preview),
  `PinWindowController`/`PinViewController` (floating pin), `Backdrop*`, `Pref*ViewController`s, `StatusMenu`.
- Annotation objects: `ProtoObject` base (color, rect, knobs, state) + `ObjectHandlers`; `Undo` (states+position).
- Frameworks: ScreenCaptureKit + CoreGraphics (capture), Vision (OCR), CoreImage (blur etc), KeyboardShortcuts pkg (hotkeys), CryptoKit/Security (license; NOT cloning).

## Core v1 scope (this build)
1. **Tray app + global hotkeys**: Area, Fullscreen, Delayed (3s), Repeat Area, Any Window / Active Window, Load from Clipboard.
2. **Area capture overlay** (RegionSelector): frozen screen image, dim, drag to select, live W x H text,
   crosshair guide lines (lguideH/V), Esc cancels, selection can be refined.
3. **After-capture behavior** (prefs cbAfterGrabCopy/Save/Show, rbAreaEditor/rbAreaPreview): copy to clipboard,
   save to default folder, open editor or small preview.
4. **Editor**: zoomable canvas with pixel/point toggle, tools and shortcut letters:
   Arrow (A), Line (L), Rectangle (R), Oval (O), Text (T), Freehand (D), Highlighter (H?),
   Counter (C), Blur/Erase (B), Spotlight (S), Magnifier (M), Ruler (1/2), Backdrop (K - later).
   Per-tool: color, size, fill/line style. Objects are selectable/movable with knobs. Undo/redo (debounced states).
5. **Crop/resize**: Hit Enter to crop, uncrop, resize.
6. **Copy / Save**: Esc or Cmd+C copies (prefs cbEscCopy/cbEscSave); save PNG or JPEG (auto/PNG), retries
   PNG full -> JPEG full -> PNG reduced -> JPEG reduced (observed strings "Attempt 1..4").
7. **Pin**: always-on-top floating image window with copy/close/edit, transparency, scroll zoom.
8. **Preferences**: default folder, hotkeys, after-grab actions, always on top, launch at startup, color format, hide menubar -> tray.

## Deferred (after core is tested)
Scrolling capture (VisionStitcher), OCR/QR (Windows.Media.Ocr), Backdrop/gradients, screenshot library/gallery,
S3/cloud upload, GIF, license/payments/telemetry (not cloned), color picker panel, ruler guides, magnifier.

## Windows mapping
| macOS | Windows |
|---|---|
| ScreenCaptureKit / CGWindowList | GDI `Graphics.CopyFromScreen` / BitBlt per monitor (DPI-aware) |
| KeyboardShortcuts | `RegisterHotKey` (user32) |
| NSStatusItem | `NotifyIcon` |
| NSWindow overlay | borderless topmost WPF window per monitor |
| NSPasteboard | `Clipboard.SetImage` |
| Vision OCR (later) | `Windows.Media.Ocr` |
