# Shutter — UI redesign

Open **Shutter Redesign.html** in a browser for the full walkthrough. Every mockup is live HTML/CSS, and the same frames are also exported as PNGs in `screens/` at 2× resolution.

## What's here

| Path | Contents |
| --- | --- |
| `Shutter Redesign.html` | Every surface, with design notes |
| `screens/` | PNG renders of each mockup (01–17) |
| `icons/` | 68 SVG icons on a 24 px grid with a 1.6 px stroke, using `currentColor` |
| `brand/` | App icon (256 px SVG) and tray icons for dark taskbars, light taskbars and capture in progress |
| `wpf/ShutterTheme.xaml` | Colours, brushes, fonts, radii, sizes and shadows as a ResourceDictionary |
| `wpf/ShutterIcons.xaml` | Every icon as WPF `Geometry` resources (`Icon.Arrow`, `Icon.Blur.Fill`, and so on) |

The PNGs were rendered with Inter because Segoe UI isn't available on the render machine. The HTML uses Segoe UI Variable when you open it on Windows.

## Layout of the editor

```
┌ Title bar 40 ─────────────────────────────────────────────────────────────┐
│ ◉ filename  size │ ↶ ↷           OCR Backdrop │ Drag Pin [Save▾] [Copy] – □ × │
├──┬────────────────────────────────────────────────────────────┬───────────┤
│R │ Context bar 44: options for the active tool / selection     │ Backdrop  │
│a ├────────────────────────────────────────────────────────────┤ panel 300 │
│i │                                                            │ (only when│
│l │                    Canvas (unchanged)                      │  it's on) │
│52│                                                            │           │
│  ├────────────────────────────────────────────────────────────┤           │
│  │ Status 28: size / messages               − 86% +  Fit 1:1  │           │
└──┴────────────────────────────────────────────────────────────┴───────────┘
```

- **Rail groups:** Select | Arrow, Line, Rectangle, Oval | Freehand, Highlighter, Text, Counter | Blur, Spotlight | Ruler, Magnifier, Picker | Crop. Resize is pinned to the bottom of the rail.
- **Uncrop** only appears in the Crop context bar, once a crop has been applied.
- **At 900 px wide:** Save, Pin, Drag and OCR move into the title bar's ••• menu. The context bar drops its labels and puts Fill and hints in its own •••. When the window is short, the rail stacks tools into flyouts: Arrow/Line, Rect/Oval, Freehand/Highlighter, Blur/Spotlight, and Ruler/Magnifier/Picker.

## Context bar by tool

| Tool | Shows |
| --- | --- |
| Select (nothing selected) | Hint: “Click an object to select · Drag to move · Double-click text to edit” |
| Arrow, Line, Freehand | Colour, Width; hint “Shift snaps to 45°” where it applies |
| Highlighter | Colour, Width |
| Rectangle, Oval | Colour, Stroke, Fill (Outline / Soft / Solid) |
| Text | Colour, Size, Background (None / Soft / Solid) |
| Counter | Colour, Size, Next number with a reset button |
| Blur / Erase | Mode (Pixelate / Erase), Block size, Erase colour (Erase mode only) |
| Spotlight | Hint only |
| Ruler | Orientation (Auto / Horizontal / Vertical), Colour |
| Magnifier | Zoom (the size slider, labelled “2×”) |
| Color picker | Live swatch and value, plus a HEX / RGB / HSL format switch |
| Crop | W × H readout, Apply (Enter), Cancel (Esc), Uncrop |
| Any object selected | A blue “<Type> selected” chip, that object's options, and Delete |

The blue chip is what makes it clear whether a change applies to the selected object or sets the default for the next one.

## WPF implementation notes

- **Custom chrome:** use `WindowChrome` with `CaptionHeight=40`, `GlassFrameThickness=0` and `ResizeBorderThickness=6`. Set `WindowChrome.IsHitTestVisibleInChrome="True"` on the title bar buttons.
- **Rounded corners and border on Windows 11:** call `DwmSetWindowAttribute` with `DWMWA_WINDOW_CORNER_PREFERENCE = DWMWCP_ROUND`. Setting `DWMWA_BORDER_COLOR` to `#2A2A2F` stops the system accent colour (your pink) from showing anywhere.
- **Snap layouts:** handle `WM_NCHITTEST` and return `HTMAXBUTTON` over the maximize button.
- **Icons:** draw each one as a `Path` in a 24×24 `Viewbox` with `Data="{StaticResource Icon.Arrow}"`, `StrokeThickness=1.6`, round caps and joins, and `Stroke` bound to the button's `Foreground`.
  - Icons with filled parts also need `Icon.X.Fill`, and some need `.FillDim` (50%), `.Dim` (45% stroke) or `.Dash` (`StrokeDashArray="1.25 1.5"`).
  - A small `IconPresenter` control that stacks these layers keeps the templates simple.
- **Rail buttons:** use `RadioButton`s in one group so exactly one tool is active.
  - The active button gets `AccentSoftBrush`, its icon turns `AccentTextBrush`, and a 3 px accent pill sits on its left edge.
  - The tooltip shows the tool name, a keycap (a `Border` with `RadiusXs`) and a one-line hint.
- **Backdrop panel:** a `Grid` column that collapses when Backdrop is off. Below about 1100 px it becomes a `Popup` anchored to the title bar button, so the canvas isn't squeezed.
- **Preferences:** settings apply immediately (the Windows 11 pattern). If you'd rather keep explicit saving, add a footer with Cancel and Save.
- **Accent colour:** the accent is used only for state (active, selected, focus, primary action), never for decoration. Copy is the only primary button in the editor.
