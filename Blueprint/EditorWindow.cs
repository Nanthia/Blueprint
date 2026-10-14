using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace Blueprint;

public enum Tool { Select, Arrow, Line, Rect, Oval, Text, Free, Highlight, Counter, Blur, Spot, Ruler, Magnifier, Picker, Ocr, Crop }

public sealed class EditorWindow : BlueprintWindow
{
    sealed record Snapshot(BitmapSource Base, List<Ann> Objects);

    // ---- canvas ----
    readonly AnnotationSurface surface = new();
    readonly Canvas overlay = new();
    readonly Grid content = new();
    readonly ScrollViewer scroller = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    readonly Grid holder = new();

    // ---- chrome pieces ----
    readonly Dictionary<Tool, RadioButton> railButtons = new();
    readonly ToggleButton ocrToggle = new() { Style = StyleOf("IconToggle") };
    readonly ToggleButton backdropToggle = new() { Style = StyleOf("IconToggle") };
    readonly Button undoBtn = new() { Style = StyleOf("IconButton") };
    readonly Button redoBtn = new() { Style = StyleOf("IconButton") };
    readonly List<FrameworkElement> collapsible = new();   // title-bar items that move to the ••• menu when narrow
    readonly Button moreBtn = new() { Style = StyleOf("IconButton"), Visibility = Visibility.Collapsed };
    readonly Grid body = new();
    readonly Border backdropHost = new() { Width = 300, Visibility = Visibility.Collapsed, Background = Ui.B("Bg2Brush"), BorderBrush = Ui.B("LineBrush"), BorderThickness = new Thickness(1, 0, 0, 0) };
    readonly Popup backdropPopup = new() { StaysOpen = true, AllowsTransparency = true, Placement = PlacementMode.Custom };
    FrameworkElement? backdropPanel;
    bool backdropInPopup;

    // ---- context bar ----
    readonly Border ctxChip = new() { CornerRadius = new CornerRadius(14), Padding = new Thickness(10, 4, 12, 4), VerticalAlignment = VerticalAlignment.Center };
    readonly StackPanel chipContent = new() { Orientation = Orientation.Horizontal };
    readonly StackPanel gColor = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    readonly StackPanel gWidth = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    readonly StackPanel gFill = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    readonly StackPanel gBlur = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    readonly StackPanel gRuler = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    readonly StackPanel gPicker = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    readonly StackPanel gCrop = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    readonly StackPanel gCounter = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    readonly StackPanel hintPanel = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0) };
    readonly Button deleteBtn = new() { Style = StyleOf("IconButton"), ToolTip = "Delete (Del)", Margin = new Thickness(0, 0, 10, 0) };
    readonly TextBlock widthLabel = new() { Foreground = Ui.B("Text3Brush"), Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock widthValue = new() { Foreground = Ui.B("Text2Brush"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    readonly Slider sizeSlider = new() { Minimum = 1, Maximum = 20, Value = 4, Width = 140 };
    readonly TextBlock fillLabel = new() { Foreground = Ui.B("Text3Brush"), Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
    readonly StackPanel fillSeg = new() { Orientation = Orientation.Horizontal };
    readonly StackPanel blurSeg = new() { Orientation = Orientation.Horizontal };
    readonly StackPanel rulerSeg = new() { Orientation = Orientation.Horizontal };
    readonly StackPanel pickFmtSeg = new() { Orientation = Orientation.Horizontal };
    readonly Border pickSwatch = new() { Width = 18, Height = 18, CornerRadius = new CornerRadius(9), BorderBrush = Ui.B("Line3Brush"), BorderThickness = new Thickness(1), Background = Brushes.Transparent, Margin = new Thickness(0, 0, 10, 0) };
    readonly TextBlock pickText = new() { Foreground = Ui.B("TextBrush"), FontFamily = new FontFamily("Cascadia Mono, Consolas"), FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, MinWidth = 150 };
    readonly TextBlock cropText = new() { Foreground = Ui.B("Text2Brush"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0), MinWidth = 90 };
    readonly TextBlock counterText = new() { Foreground = Ui.B("Text2Brush"), VerticalAlignment = VerticalAlignment.Center };
    readonly Button uncropBtn = new();
    Border? ctxBar;
    readonly StackPanel ctxLeftRow = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    readonly List<(Color color, Ellipse ring)> swatchRings = new();
    Ellipse? customRing;
    bool suppress;
    bool eraseColorChosen;           // erase with a picked colour (false = automatic surrounding colour)
    readonly Button eraseAutoBtn = new() { Content = "Auto", MinHeight = 28, Padding = new Thickness(10, 0, 10, 0), Margin = new Thickness(8, 0, 0, 0), ToolTip = "Match the colour around the box" };

    // ---- status bar ----
    readonly StackPanel statusLeft = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 0, 0) };
    readonly TextBlock statusText = new() { Foreground = Ui.B("Text2Brush"), VerticalAlignment = VerticalAlignment.Center, FontSize = 12 };
    readonly Border statusSwatch = new() { Width = 12, Height = 12, CornerRadius = new CornerRadius(6), Margin = new Thickness(0, 0, 8, 0), Visibility = Visibility.Collapsed };
    readonly TextBlock zoomText = new() { Foreground = Ui.B("Text2Brush"), VerticalAlignment = VerticalAlignment.Center, MinWidth = 44, TextAlignment = TextAlignment.Center, FontSize = 12 };
    readonly DispatcherTimer statusTimer = new() { Interval = TimeSpan.FromSeconds(3.5) };
    IconPresenter? statusIcon;

    // ---- state ----
    readonly List<Snapshot> undo = new(), redo = new();
    Tool tool = Tool.Arrow;
    Color color = InkRed;
    double zoom = 1, dpiScale = Native.SystemScale;
    int fillMode, blurMode;
    int rulerAxis; // 0 auto, 1 horizontal, 2 vertical

    enum Drag { None, Create, Move, Handle, Crop }
    Drag drag;
    Point startPt, lastPt;
    int handleIdx;
    bool movePushed;
    long lastAttrUndo;

    TextBox? textBox;
    TextAnn? editingText;

    BackdropSettings bd = Settings.Current.Backdrop.Clone();

    // uncrop: the full image before the (cumulative) crop and where the crop sits inside it
    BitmapSource? uncropFull;
    int uncropX, uncropY;

    // colour picker pixel cache
    byte[]? pix;
    BitmapSource? pixSrc;
    int pixStride;

    static readonly Color InkRed = Color.FromRgb(0xFF, 0x45, 0x3A);
    static readonly Color[] Palette =
    {
        InkRed, Color.FromRgb(0xFF, 0x9F, 0x0A), Color.FromRgb(0xFF, 0xD6, 0x0A), Color.FromRgb(0x32, 0xD7, 0x4B),
        Color.FromRgb(0x0A, 0x84, 0xFF), Color.FromRgb(0xBF, 0x5A, 0xF2), Color.FromRgb(0x1C, 0x1C, 0x1E), Colors.White,
    };

    static Style StyleOf(string key) => (Style)Application.Current.FindResource(key);

    public EditorWindow(BitmapSource image, string? name = null)
    {
        Title = name ?? "Screenshot " + DateTime.Now.ToString("yyyy-MM-dd HHmmss");
        Topmost = Settings.Current.AlwaysOnTop;
        MinWidth = 860; MinHeight = 520;

        bd.Enabled = false;
        surface.Backdrop = bd;
        surface.LayoutChanged += () => overlay.Margin = new Thickness(surface.Origin.X, surface.Origin.Y, 0, 0);
        overlay.HorizontalAlignment = HorizontalAlignment.Left;
        overlay.VerticalAlignment = VerticalAlignment.Top;
        surface.SetBase(image);

        BuildTitleBar();
        BuildBody();
        InitialSize(image);

        PreviewKeyDown += OnKeyDown;
        Loaded += (_, _) => { dpiScale = VisualTreeHelper.GetDpi(this).DpiScaleX; ApplyZoom(); UpdateLayoutMode(); Activate(); };
        SizeChanged += (_, _) => UpdateLayoutMode();
        scroller.ScrollChanged += (_, _) => { holder.MinWidth = scroller.ViewportWidth; holder.MinHeight = scroller.ViewportHeight; };
        statusTimer.Tick += (_, _) => { statusTimer.Stop(); ShowDefaultStatus(); };
        SelectTool(Tool.Arrow);
        SetSwatch(color);
        UpdateTitle();
        UpdateUndoUi();
        ShowDefaultStatus();
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        dpiScale = newDpi.DpiScaleX;
        ApplyZoom();
    }

    // =====================================================================
    //  Layout
    // =====================================================================

    static Button IconBtn(string icon, string tip, Action a, double size = 18)
    {
        var b = new Button { Style = StyleOf("IconButton"), ToolTip = tip, Content = Ui.Icon(icon, size) };
        b.Click += (_, _) => a();
        return b;
    }

    static FrameworkElement VSep(double h = 20) => new Border { Width = 1, Height = h, Background = Ui.B("Line2Brush"), Margin = new Thickness(8, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };

    void BuildTitleBar()
    {
        // left: undo / redo
        undoBtn.Content = Ui.Icon("Undo", 18); undoBtn.ToolTip = RichTip("Undo", "Ctrl+Z", null);
        redoBtn.Content = Ui.Icon("Redo", 18); redoBtn.ToolTip = RichTip("Redo", "Ctrl+Y", null);
        undoBtn.Click += (_, _) => Undo();
        redoBtn.Click += (_, _) => Redo();
        var left = new StackPanel { Orientation = Orientation.Horizontal };
        left.Children.Add(VSep(18));
        left.Children.Add(undoBtn);
        left.Children.Add(redoBtn);
        TitleLeft = left;

        // right: OCR, backdrop | drag, pin | save split, copy
        ocrToggle.Content = Ui.Icon("Ocr", 18);
        ocrToggle.ToolTip = RichTip("Recognize text / QR", "Q", "Drag a rectangle; the text is copied");
        ocrToggle.Click += (_, _) => SelectTool(ocrToggle.IsChecked == true ? Tool.Ocr : Tool.Select);
        backdropToggle.Content = Ui.Icon("Backdrop", 18);
        backdropToggle.ToolTip = RichTip("Backdrop", "K", "A pretty background around the screenshot");
        backdropToggle.Click += (_, _) => SetBackdrop(backdropToggle.IsChecked == true);

        var drag = MakeDragOut();
        var pin = IconBtn("Pin", "Float this image on top (Ctrl+P)", PinIt);

        var save = SaveSplit();
        var copy = new Button { Style = StyleOf("PrimaryButton"), Margin = new Thickness(8, 0, 0, 0), Height = 32, Padding = new Thickness(14, 0, 16, 0), ToolTip = "Copy to clipboard (Ctrl+C)" };
        copy.Content = Ui.IconLabel("Copy", "Copy", 16, 8);
        copy.Click += (_, _) => CopyOut();

        moreBtn.Content = Ui.Icon("More", 18);
        moreBtn.ToolTip = "More";
        moreBtn.Click += (_, _) => ShowMoreMenu();

        var right = new StackPanel { Orientation = Orientation.Horizontal };
        right.Children.Add(ocrToggle);
        right.Children.Add(backdropToggle);
        var sep1 = VSep(18); right.Children.Add(sep1);
        right.Children.Add(drag);
        right.Children.Add(pin);
        right.Children.Add(moreBtn);
        right.Children.Add(save);
        right.Children.Add(copy);
        collapsible.AddRange(new FrameworkElement[] { ocrToggle, sep1, drag, pin, save });
        TitleRight = right;
    }

    FrameworkElement SaveSplit()
    {
        var box = new Border { Background = Ui.B("Bg3Brush"), BorderBrush = Ui.B("Line2Brush"), BorderThickness = new Thickness(1), CornerRadius = Ui.Radius("RadiusSm"), Margin = new Thickness(6, 0, 0, 0), Height = 32 };
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var main = new Button { Style = StyleOf("GhostButton"), Foreground = Ui.B("TextBrush"), Padding = new Thickness(12, 0, 10, 0), ToolTip = "Save to the screenshots folder (Ctrl+S)", MinHeight = 30, Content = Ui.IconLabel("Save", "Save", 16, 8) };
        main.Click += (_, _) => SaveOut();
        var chev = new Button { Style = StyleOf("GhostButton"), Padding = new Thickness(6, 0, 6, 0), MinHeight = 30, ToolTip = "More save options", Content = Ui.Icon("Chevron", 16) };
        chev.Click += (_, _) =>
        {
            var m = new ContextMenu { PlacementTarget = box, Placement = PlacementMode.Bottom };
            m.Items.Add(Item("Save", "Save", "Ctrl+S", SaveOut));
            m.Items.Add(Item("Save as…", "Save", "Ctrl+Shift+S", SaveAs));
            m.Items.Add(new Separator());
            m.Items.Add(Item("Open screenshots folder", "Folder", null, () =>
            {
                try { Directory.CreateDirectory(Settings.Current.Folder); System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", "\"" + Settings.Current.Folder + "\"") { UseShellExecute = true }); } catch { }
            }));
            m.IsOpen = true;
        };
        row.Children.Add(main);
        row.Children.Add(new Border { Width = 1, Background = Ui.B("Line2Brush"), Margin = new Thickness(0, 6, 0, 6) });
        row.Children.Add(chev);
        box.Child = row;
        return box;
    }

    static MenuItem Item(string header, string icon, string? gesture, Action a, bool danger = false)
    {
        var mi = new MenuItem { Header = header, Icon = Ui.Icon(icon, 16, Ui.B(danger ? "DangerBrush" : "Text2Brush")), InputGestureText = gesture ?? "" };
        if (danger) mi.Style = StyleOf("DangerMenuItem");
        mi.Click += (_, _) => a();
        return mi;
    }

    void ShowMoreMenu()
    {
        var m = new ContextMenu { PlacementTarget = moreBtn, Placement = PlacementMode.Bottom };
        m.Items.Add(Item("Recognize text / QR", "Ocr", "Q", () => SelectTool(Tool.Ocr)));
        m.Items.Add(Item("Pin to screen", "Pin", "Ctrl+P", PinIt));
        m.Items.Add(new Separator());
        m.Items.Add(Item("Save", "Save", "Ctrl+S", SaveOut));
        m.Items.Add(Item("Save as…", "Save", "Ctrl+Shift+S", SaveAs));
        m.IsOpen = true;
    }

    static ToolTip RichTip(string name, string? key, string? hint)
    {
        var sp = new StackPanel();
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(new TextBlock { Text = name, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        if (!string.IsNullOrEmpty(key))
        {
            var kc = key!.Contains('+') ? (FrameworkElement)Ui.Keys(key) : Ui.Keycap(key);
            kc.Margin = new Thickness(10, 0, 0, 0);
            row.Children.Add(kc);
        }
        sp.Children.Add(row);
        if (!string.IsNullOrEmpty(hint)) sp.Children.Add(new TextBlock { Text = hint, Foreground = Ui.B("Text3Brush"), Margin = new Thickness(0, 3, 0, 0), FontSize = 12 });
        var tip = new ToolTip { Content = sp };
        return tip;
    }

    FrameworkElement MakeRail()
    {
        var stack = new StackPanel { Margin = new Thickness(8, 8, 8, 8) };
        void Group(params (Tool t, string icon, string name, string key, string hint)[] items)
        {
            if (stack.Children.Count > 0) stack.Children.Add(new Border { Height = 1, Background = Ui.B("Line2Brush"), Margin = new Thickness(4, 6, 4, 6) });
            foreach (var (t, icon, name, key, hint) in items)
            {
                var rb = new RadioButton { Style = StyleOf("RailButton"), GroupName = "tools", Content = Ui.Icon(icon, 20), ToolTip = RichTip(name, key, hint) };
                var tt = t;
                rb.Click += (_, _) => { if (tt == Tool.Ruler) rulerAxis = 0; SelectTool(tt); };
                railButtons[t] = rb;
                stack.Children.Add(rb);
            }
        }
        Group((Tool.Select, "Select", "Select", "V", "Select, move and resize objects"));
        Group((Tool.Arrow, "Arrow", "Arrow", "A", "Tapered arrow"), (Tool.Line, "Line", "Line", "L", "Straight line"),
              (Tool.Rect, "Rect", "Rectangle", "R", "Outline, soft or solid fill"), (Tool.Oval, "Oval", "Oval", "O", "Outline, soft or solid fill"));
        Group((Tool.Free, "Freehand", "Freehand", "D", "Smoothed pen"), (Tool.Highlight, "Highlighter", "Highlighter", "H", "Wide translucent stroke"),
              (Tool.Text, "Text", "Text", "T", "Click to type on the image"), (Tool.Counter, "Counter", "Counter", "C", "Numbered markers"));
        Group((Tool.Blur, "Blur", "Blur / Erase", "B", "Pixelate a region, or switch to Erase in the bar above to cover it with a solid colour"), (Tool.Spot, "Spotlight", "Spotlight", "S", "Dim everything outside a rectangle"));
        Group((Tool.Ruler, "Ruler", "Ruler", "1 / 2", "Measure pixel distances"), (Tool.Magnifier, "Magnifier", "Magnifier", "M", "Zoom into a detail"),
              (Tool.Picker, "Picker", "Color picker", "I", "Click to copy a colour"));
        Group((Tool.Crop, "Crop", "Crop", "X", "Drag a region, Enter to apply"));

        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, Content = stack };

        var resize = IconBtn("Resize", "Resize image (Ctrl+R)", ResizeImage, 20);
        resize.Width = 36; resize.Height = 36; resize.HorizontalAlignment = HorizontalAlignment.Center; resize.Margin = new Thickness(0, 0, 0, 10);
        var dock = new DockPanel();
        DockPanel.SetDock(resize, Dock.Bottom);
        dock.Children.Add(resize);
        dock.Children.Add(scroll);
        return new Border { Width = 52, Background = Ui.B("Bg2Brush"), BorderBrush = Ui.B("LineBrush"), BorderThickness = new Thickness(0, 0, 1, 0), Child = dock };
    }

    FrameworkElement MakeContextBar()
    {
        // chip
        chipContent.Children.Add(Ui.Icon("Arrow", 16));
        chipContent.Children.Add(new TextBlock { Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.SemiBold });
        ctxChip.Child = chipContent;

        // colours
        foreach (var c in Palette) gColor.Children.Add(MakeSwatch(c));
        var custom = new Grid { Width = 28, Height = 28, Cursor = Cursors.Hand, Margin = new Thickness(1, 0, 1, 0), ToolTip = "Custom colour…" };
        customRing = new Ellipse { Width = 28, Height = 28, Stroke = Ui.B("AccentBrush"), StrokeThickness = 2, Visibility = Visibility.Hidden };
        custom.Children.Add(customRing);
        custom.Children.Add(new Ellipse
        {
            Width = 20, Height = 20,
            Fill = new LinearGradientBrush(new GradientStopCollection
            {
                new(Color.FromRgb(0xFF, 0x45, 0x3A), 0), new(Color.FromRgb(0xFF, 0xD6, 0x0A), 0.25), new(Color.FromRgb(0x32, 0xD7, 0x4B), 0.5),
                new(Color.FromRgb(0x0A, 0x84, 0xFF), 0.75), new(Color.FromRgb(0xBF, 0x5A, 0xF2), 1),
            }, 45),
        });
        custom.MouseLeftButtonDown += (_, _) => PickCustomColor();
        gColor.Children.Add(custom);
        eraseAutoBtn.Click += (_, _) => { eraseColorChosen = false; AttrChanged(a => { if (a is BlurAnn b) b.UseColor = false; }); RefreshContext(); };
        gColor.Children.Add(eraseAutoBtn);

        // width
        gWidth.Children.Add(widthLabel);
        sizeSlider.ValueChanged += (_, _) =>
        {
            UpdateWidthValue();
            if (!suppress) AttrChanged(a => a.Size = sizeSlider.Value);
        };
        gWidth.Children.Add(sizeSlider);
        var wbox = new Border { Background = Ui.B("Bg0Brush"), BorderBrush = Ui.B("Line2Brush"), BorderThickness = new Thickness(1), CornerRadius = Ui.Radius("RadiusSm"), Width = 40, Height = 28, Margin = new Thickness(10, 0, 0, 0), Child = widthValue };
        gWidth.Children.Add(wbox);

        // fill (3 segments, labels change per tool)
        gFill.Children.Add(fillLabel);
        gFill.Children.Add(new Border { Style = StyleOf("SegmentBox"), Child = fillSeg });
        for (int i = 0; i < 3; i++)
        {
            var rb = new RadioButton { Style = StyleOf("SegmentItem"), GroupName = "fillseg", Content = "" };
            int idx = i;
            rb.Checked += (_, _) => { if (suppress) return; fillMode = idx; AttrChanged(a => { if (a.SupportsFill && a is not BlurAnn) a.FillMode = fillMode; }); };
            fillSeg.Children.Add(rb);
        }

        // blur mode
        gBlur.Children.Add(new TextBlock { Text = "Mode", Foreground = Ui.B("Text3Brush"), Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center });
        gBlur.Children.Add(new Border { Style = StyleOf("SegmentBox"), Child = blurSeg });
        foreach (var (label, idx) in new[] { ("Pixelate", 0), ("Erase", 1) })
        {
            var rb = new RadioButton { Style = StyleOf("SegmentItem"), GroupName = "blurseg", Content = label };
            int i2 = idx;
            rb.Checked += (_, _) => { if (suppress) return; blurMode = i2; AttrChanged(a => { if (a is BlurAnn) a.FillMode = blurMode; }); };
            blurSeg.Children.Add(rb);
        }

        // ruler orientation
        gRuler.Children.Add(new TextBlock { Text = "Orientation", Foreground = Ui.B("Text3Brush"), Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center });
        gRuler.Children.Add(new Border { Style = StyleOf("SegmentBox"), Child = rulerSeg });
        foreach (var (label, idx) in new[] { ("Auto", 0), ("Horizontal", 1), ("Vertical", 2) })
        {
            var rb = new RadioButton { Style = StyleOf("SegmentItem"), GroupName = "rulerseg", Content = label };
            int i2 = idx;
            rb.Checked += (_, _) => { if (suppress) return; rulerAxis = i2; AttrChanged(a => { if (a is RulerAnn r) r.Axis = rulerAxis; }); };
            rulerSeg.Children.Add(rb);
        }

        // colour picker
        gPicker.Children.Add(pickSwatch);
        gPicker.Children.Add(pickText);
        gPicker.Children.Add(new Border { Style = StyleOf("SegmentBox"), Child = pickFmtSeg });
        foreach (var f in new[] { "HEX", "RGB", "HSL" })
        {
            var rb = new RadioButton { Style = StyleOf("SegmentItem"), GroupName = "pickfmt", Content = f, IsChecked = f.Equals(Settings.Current.ColorFormat, StringComparison.OrdinalIgnoreCase) };
            var ff = f;
            rb.Checked += (_, _) => { Settings.Current.ColorFormat = ff.ToLowerInvariant(); Settings.Current.Save(); };
            pickFmtSeg.Children.Add(rb);
        }

        // crop
        gCrop.Children.Add(cropText);
        var apply = new Button { Style = StyleOf("PrimaryButton"), Content = Ui.IconLabel("Check", "Apply", 15, 6), MinHeight = 28, Padding = new Thickness(12, 0, 12, 0), ToolTip = "Apply the crop (Enter)" };
        apply.Click += (_, _) => ApplyCrop();
        var cancel = new Button { Content = "Cancel", MinHeight = 28, Padding = new Thickness(12, 0, 12, 0), Margin = new Thickness(6, 0, 0, 0), ToolTip = "Cancel (Esc)" };
        cancel.Click += (_, _) => { surface.CropRect = null; surface.InvalidateVisual(); RefreshContext(); };
        uncropBtn.Style = StyleOf("GhostButton"); uncropBtn.Content = Ui.IconLabel("Uncrop", "Uncrop", 15, 6); uncropBtn.MinHeight = 28; uncropBtn.Margin = new Thickness(6, 0, 0, 0); uncropBtn.ToolTip = "Restore the area removed by crop (U)";
        uncropBtn.Click += (_, _) => Uncrop();
        gCrop.Children.Add(apply); gCrop.Children.Add(cancel); gCrop.Children.Add(uncropBtn);

        gCounter.Children.Add(counterText);

        deleteBtn.Content = Ui.Icon("Trash", 18);
        deleteBtn.Click += (_, _) => DeleteSelected();

        // assemble
        var leftRow = ctxLeftRow;
        void Add(FrameworkElement g, bool sep = true)
        {
            if (sep) leftRow.Children.Add(VSep(20));
            leftRow.Children.Add(g);
        }
        leftRow.Children.Add(ctxChip);
        Add(gColor); Add(gWidth); Add(gFill); Add(gBlur); Add(gRuler); Add(gPicker); Add(gCrop); Add(gCounter);
        // separators sit between groups; hide those whose neighbour group is hidden
        foreach (var g in new[] { gColor, gWidth, gFill, gBlur, gRuler, gPicker, gCrop, gCounter })
            g.IsVisibleChanged += (_, _) => SyncSeparators(leftRow);

        var right = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        right.Children.Add(hintPanel);
        right.Children.Add(deleteBtn);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(leftRow);
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);

        ctxBar = new Border { Height = 44, Background = Ui.B("Bg1Brush"), BorderBrush = Ui.B("LineBrush"), BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(14, 0, 0, 0), Child = grid, ClipToBounds = true };
        ctxBar.SizeChanged += (_, _) => UpdateHint();
        return ctxBar;
    }

    static void SyncSeparators(StackPanel row)
    {
        for (int i = 1; i < row.Children.Count; i++)
            if (row.Children[i] is Border b && b.Width == 1)
                b.Visibility = i + 1 < row.Children.Count && row.Children[i + 1].Visibility == Visibility.Visible ? Visibility.Visible : Visibility.Collapsed;
    }

    FrameworkElement MakeSwatch(Color c)
    {
        var g = new Grid { Width = 28, Height = 28, Cursor = Cursors.Hand, Margin = new Thickness(1, 0, 1, 0), Background = Brushes.Transparent };
        var ring = new Ellipse { Width = 28, Height = 28, Stroke = Ui.B("AccentBrush"), StrokeThickness = 2, Visibility = Visibility.Hidden };
        g.Children.Add(ring);
        g.Children.Add(new Ellipse { Width = 20, Height = 20, Fill = new SolidColorBrush(c), Stroke = Ui.B("Line3Brush"), StrokeThickness = c == Palette[6] ? 1 : 0 });
        g.MouseLeftButtonDown += (_, _) => PickColor(c);
        swatchRings.Add((c, ring));
        return g;
    }

    FrameworkElement MakeStatusBar()
    {
        statusLeft.Children.Add(statusSwatch);
        statusIcon = Ui.Icon("Check", 14, Ui.B("SuccessBrush"));
        statusIcon.Margin = new Thickness(0, 0, 8, 0); statusIcon.VerticalAlignment = VerticalAlignment.Center; statusIcon.Visibility = Visibility.Collapsed;
        statusLeft.Children.Add(statusIcon);
        statusLeft.Children.Add(statusText);

        var zoomRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 8, 0) };
        void Z(string icon, string tip, Action a, double s = 14) { var b = IconBtn(icon, tip, a, s); b.Width = 24; b.Height = 22; zoomRow.Children.Add(b); }
        Z("Zoomout", "Zoom out (Ctrl+−)", () => { zoom /= 1.25; ApplyZoom(); });
        zoomRow.Children.Add(zoomText);
        Z("Zoomin", "Zoom in (Ctrl++)", () => { zoom *= 1.25; ApplyZoom(); });
        zoomRow.Children.Add(VSep(14));
        Z("Fit", "Fit to window", FitZoom, 16);
        Z("Actual", "Actual size (Ctrl+0)", () => { zoom = 1; ApplyZoom(); }, 16);

        var g = new Grid();
        g.Children.Add(statusLeft);
        g.Children.Add(zoomRow);
        return new Border { Height = 28, Background = Ui.B("Bg2Brush"), BorderBrush = Ui.B("LineBrush"), BorderThickness = new Thickness(0, 1, 0, 0), Child = g };
    }

    void BuildBody()
    {
        content.Children.Add(surface);
        content.Children.Add(overlay);
        content.HorizontalAlignment = HorizontalAlignment.Center;
        content.VerticalAlignment = VerticalAlignment.Center;
        holder.Children.Add(content);
        scroller.Content = holder;
        scroller.PreviewMouseWheel += OnWheel;
        scroller.Background = Ui.B("Bg0Brush");
        surface.MouseLeftButtonDown += OnDown;
        surface.MouseMove += OnMove;
        surface.MouseLeftButtonUp += OnUp;

        var ctx = MakeContextBar();
        var status = MakeStatusBar();

        var main = new DockPanel();
        DockPanel.SetDock(ctx, Dock.Top);
        DockPanel.SetDock(status, Dock.Bottom);
        main.Children.Add(ctx);
        main.Children.Add(status);
        main.Children.Add(scroller);

        body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        body.ColumnDefinitions.Add(new ColumnDefinition());
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var rail = MakeRail();
        body.Children.Add(rail);
        Grid.SetColumn(main, 1);
        body.Children.Add(main);
        Grid.SetColumn(backdropHost, 2);
        body.Children.Add(backdropHost);
        Content = body;

        backdropPopup.PlacementTarget = backdropToggle;
        backdropPopup.CustomPopupPlacementCallback = (popupSize, targetSize, _) =>
            new[] { new CustomPopupPlacement(new Point(targetSize.Width - popupSize.Width, targetSize.Height + 8), PopupPrimaryAxis.None) };
        RebuildBackdropPanel();
    }

    void InitialSize(BitmapSource img)
    {
        var wa = SystemParameters.WorkArea;
        double s = Native.SystemScale;
        double fit = Math.Min(1, Math.Min((wa.Width * s - 120) * 0.92 / img.PixelWidth, (wa.Height * s - 220) * 0.9 / img.PixelHeight));
        zoom = Math.Max(0.05, fit);
        Width = Math.Clamp(img.PixelWidth * zoom / s + 52 + 120, Math.Min(1180, wa.Width), wa.Width);
        Height = Math.Clamp(img.PixelHeight * zoom / s + 40 + 44 + 28 + 100, Math.Min(740, wa.Height), wa.Height);
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
    }

    /// <summary>Wide: backdrop is a side panel; narrow: popover. Very narrow: title-bar actions fold into the ••• menu.</summary>
    void UpdateLayoutMode()
    {
        bool narrow = ActualWidth < 1000;
        foreach (var e in collapsible) e.Visibility = narrow ? Visibility.Collapsed : Visibility.Visible;
        moreBtn.Visibility = narrow ? Visibility.Visible : Visibility.Collapsed;
        bool wantPopup = ActualWidth < 1100;
        if (wantPopup != backdropInPopup) { backdropInPopup = wantPopup; PlaceBackdropPanel(); }
    }

    // =====================================================================
    //  Context bar
    // =====================================================================

    sealed record Opts(string Name, string Icon, bool Color = false, string? WidthLabel = null, string[]? FillLabels = null, string? FillTitle = null,
                       bool Blur = false, bool Ruler = false, bool Picker = false, bool Crop = false, bool Counter = false, (string key, string text)[]? Hint = null);

    static Opts OptsForTool(Tool t) => t switch
    {
        Tool.Select => new("Select", "Select", Hint: new[] { ("", "Click an object to select · Drag to move · Double-click text to edit") }),
        Tool.Arrow => new("Arrow", "Arrow", Color: true, WidthLabel: "Width", Hint: new[] { ("Shift", "snap to 45°") }),
        Tool.Line => new("Line", "Line", Color: true, WidthLabel: "Width", Hint: new[] { ("Shift", "snap to 45°") }),
        Tool.Rect => new("Rectangle", "Rect", Color: true, WidthLabel: "Stroke", FillLabels: new[] { "Outline", "Soft", "Solid" }, FillTitle: "Fill", Hint: new[] { ("Shift", "square") }),
        Tool.Oval => new("Oval", "Oval", Color: true, WidthLabel: "Stroke", FillLabels: new[] { "Outline", "Soft", "Solid" }, FillTitle: "Fill", Hint: new[] { ("Shift", "circle") }),
        Tool.Free => new("Freehand", "Freehand", Color: true, WidthLabel: "Width"),
        Tool.Highlight => new("Highlighter", "Highlighter", Color: true, WidthLabel: "Width"),
        Tool.Text => new("Text", "Text", Color: true, WidthLabel: "Size", FillLabels: new[] { "None", "Soft", "Solid" }, FillTitle: "Background", Hint: new[] { ("Enter", "place"), ("Shift+Enter", "new line") }),
        Tool.Counter => new("Counter", "Counter", Color: true, WidthLabel: "Size", Counter: true),
        Tool.Blur => new("Blur / Erase", "Blur", WidthLabel: "Block size", Blur: true),
        Tool.Spot => new("Spotlight", "Spotlight", Hint: new[] { ("", "Drag a rectangle to dim everything else") }),
        Tool.Ruler => new("Ruler", "Ruler", Color: true, Ruler: true, Hint: new[] { ("1", "horizontal"), ("2", "vertical") }),
        Tool.Magnifier => new("Magnifier", "Magnifier", WidthLabel: "Zoom", Hint: new[] { ("", "Drag from the centre outwards") }),
        Tool.Picker => new("Color picker", "Picker", Picker: true),
        Tool.Ocr => new("Recognize text", "Ocr", Hint: new[] { ("", "Drag a rectangle over text or a QR code · click for the whole image") }),
        Tool.Crop => new("Crop", "Crop", Crop: true, Hint: new[] { ("Enter", "apply"), ("Esc", "cancel") }),
        _ => new("", "Select"),
    };

    static string TypeName(Ann a) => a switch
    {
        ArrowAnn => "Arrow", LineAnn => "Line", RectAnn => "Rectangle", OvalAnn => "Oval", TextAnn => "Text", CounterAnn => "Counter",
        FreeAnn f => f.Highlighter ? "Highlighter" : "Freehand", BlurAnn => "Blur", SpotlightAnn => "Spotlight", RulerAnn => "Ruler", MagnifierAnn => "Magnifier", _ => "Object",
    };

    static Tool ToolOf(Ann a) => a switch
    {
        ArrowAnn => Tool.Arrow, LineAnn => Tool.Line, RectAnn => Tool.Rect, OvalAnn => Tool.Oval, TextAnn => Tool.Text, CounterAnn => Tool.Counter,
        FreeAnn f => f.Highlighter ? Tool.Highlight : Tool.Free, BlurAnn => Tool.Blur, SpotlightAnn => Tool.Spot, RulerAnn => Tool.Ruler, MagnifierAnn => Tool.Magnifier, _ => Tool.Select,
    };

    void RefreshContext()
    {
        var sel = surface.Selected;
        bool showSelection = tool == Tool.Select && sel != null;
        var o = OptsForTool(showSelection ? ToolOf(sel!) : tool);

        // chip
        chipContent.Children.Clear();
        if (showSelection)
        {
            ctxChip.Background = Ui.B("AccentSoftBrush");
            chipContent.Children.Add(new Ellipse { Width = 7, Height = 7, Fill = Ui.B("AccentBrush"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
            chipContent.Children.Add(new TextBlock { Text = TypeName(sel!) + " selected", Foreground = Ui.B("AccentTextBrush"), FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        }
        else
        {
            ctxChip.Background = Brushes.Transparent;
            chipContent.Children.Add(Ui.Icon(o.Icon, 16, Ui.B("Text2Brush")));
            chipContent.Children.Add(new TextBlock { Text = o.Name, FontWeight = FontWeights.SemiBold, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        }

        // sync controls to the selected object
        suppress = true;
        if (showSelection)
        {
            if (sel!.SupportsFill && sel is not BlurAnn) fillMode = sel.FillMode;
            if (sel is BlurAnn) blurMode = sel.FillMode;
            sizeSlider.Value = Math.Clamp(sel.Size, 1, 20);
            if (sel is RulerAnn ra) rulerAxis = ra.Axis;
            if (!(sel is SpotlightAnn or BlurAnn)) { color = sel.Color; }
        }
        bool eraseMode = (o.Blur && blurMode == 1) || (showSelection && sel is BlurAnn { FillMode: 1 });
        gColor.Visibility = o.Color || eraseMode ? Visibility.Visible : Visibility.Collapsed;
        eraseAutoBtn.Visibility = eraseMode ? Visibility.Visible : Visibility.Collapsed;
        eraseAutoBtn.Opacity = eraseColorChosen ? 0.6 : 1;
        gWidth.Visibility = o.WidthLabel != null ? Visibility.Visible : Visibility.Collapsed;
        widthLabel.Text = o.WidthLabel ?? "";
        gFill.Visibility = o.FillLabels != null ? Visibility.Visible : Visibility.Collapsed;
        if (o.FillLabels != null)
        {
            fillLabel.Text = o.FillTitle ?? "Fill";
            for (int i = 0; i < 3; i++)
            {
                var rb = (RadioButton)fillSeg.Children[i];
                rb.Content = o.FillLabels[i];
                rb.IsChecked = i == Math.Min(fillMode, 2);
            }
        }
        gBlur.Visibility = o.Blur ? Visibility.Visible : Visibility.Collapsed;
        if (o.Blur) ((RadioButton)blurSeg.Children[Math.Min(blurMode, 1)]).IsChecked = true;
        gRuler.Visibility = o.Ruler ? Visibility.Visible : Visibility.Collapsed;
        if (o.Ruler) ((RadioButton)rulerSeg.Children[rulerAxis]).IsChecked = true;
        gPicker.Visibility = o.Picker ? Visibility.Visible : Visibility.Collapsed;
        gCrop.Visibility = o.Crop ? Visibility.Visible : Visibility.Collapsed;
        uncropBtn.Visibility = uncropFull != null ? Visibility.Visible : Visibility.Collapsed;
        gCounter.Visibility = o.Counter ? Visibility.Visible : Visibility.Collapsed;
        suppress = false;
        UpdateCounterText();
        UpdateWidthValue();
        UpdateCropText();
        SetSwatch(color);
        if (eraseMode && !(showSelection && sel is BlurAnn { UseColor: true }) && !eraseColorChosen)
        {
            // Auto erase colour: no swatch is "selected"
            foreach (var (_, ring) in swatchRings) ring.Visibility = Visibility.Hidden;
            if (customRing != null) customRing.Visibility = Visibility.Hidden;
        }
        eraseAutoBtn.Background = eraseMode && !eraseColorChosen ? Ui.B("Bg4Brush") : Ui.B("Bg3Brush");
        deleteBtn.Visibility = showSelection ? Visibility.Visible : Visibility.Collapsed;

        // hint
        hintPanel.Children.Clear();
        if (o.Hint != null)
            foreach (var (key, text) in o.Hint)
            {
                if (!string.IsNullOrEmpty(key)) hintPanel.Children.Add(key.Contains('+') ? Ui.Keys(key) : Ui.Keycap(key));
                hintPanel.Children.Add(new TextBlock { Text = text, Foreground = Ui.B("Text3Brush"), Margin = new Thickness(4, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center, FontSize = 12 });
            }
        SyncSeparators(ctxLeftRow);
        Dispatcher.BeginInvoke(new Action(UpdateHint), DispatcherPriority.Loaded);
    }

    /// <summary>Keep the keyboard hint only when there is room for it next to the options.</summary>
    void UpdateHint()
    {
        if (ctxBar == null) return;
        hintPanel.Visibility = hintPanel.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (hintPanel.Children.Count == 0) return;
        ctxLeftRow.Measure(new Size(double.PositiveInfinity, 44));
        hintPanel.Measure(new Size(double.PositiveInfinity, 44));
        double need = ctxLeftRow.DesiredSize.Width + hintPanel.DesiredSize.Width + (deleteBtn.Visibility == Visibility.Visible ? 48 : 0) + 40;
        if (ctxBar.ActualWidth > 0 && ctxBar.ActualWidth < need) hintPanel.Visibility = Visibility.Collapsed;
    }

    void UpdateWidthValue()
    {
        bool magnifier = tool == Tool.Magnifier || (tool == Tool.Select && surface.Selected is MagnifierAnn);
        widthValue.Text = magnifier ? $"{1 + sizeSlider.Value / 3:0.#}×" : ((int)sizeSlider.Value).ToString();
    }

    void UpdateCounterText()
    {
        int next = surface.Objects.OfType<CounterAnn>().Select(c => c.Number).DefaultIfEmpty(0).Max() + 1;
        counterText.Text = "Next number  " + next;
    }

    void UpdateCropText()
    {
        if (surface.CropRect is { } r && r.Width > 0) cropText.Text = $"{(int)r.Width} × {(int)r.Height} px";
        else cropText.Text = "Drag a region";
    }

    // =====================================================================
    //  Title / status
    // =====================================================================

    void UpdateTitle() => Subtitle = $"{surface.Base.PixelWidth} × {surface.Base.PixelHeight}";

    void ShowDefaultStatus()
    {
        statusIcon!.Visibility = Visibility.Collapsed;
        statusSwatch.Visibility = Visibility.Collapsed;
        int n = surface.Objects.Count;
        string dims = bd.Enabled ? $"{(int)surface.CanvasSize.Width} × {(int)surface.CanvasSize.Height} px with backdrop" : $"{surface.Base.PixelWidth} × {surface.Base.PixelHeight} px";
        statusText.Text = n > 0 ? $"{dims}  •  {n} annotation{(n == 1 ? "" : "s")}" : dims;
        statusText.Foreground = Ui.B("Text2Brush");
    }

    void Status(string msg, bool ok = false)
    {
        statusSwatch.Visibility = Visibility.Collapsed;
        statusIcon!.Visibility = ok ? Visibility.Visible : Visibility.Collapsed;
        statusText.Text = msg;
        statusText.Foreground = Ui.B(ok ? "TextBrush" : "Text2Brush");
        statusTimer.Stop(); statusTimer.Start();
    }

    // =====================================================================
    //  Zoom
    // =====================================================================

    void ApplyZoom()
    {
        zoom = Math.Clamp(zoom, 0.05, 16);
        content.LayoutTransform = new ScaleTransform(zoom / dpiScale, zoom / dpiScale);
        RenderOptions.SetBitmapScalingMode(surface, zoom >= 2 ? BitmapScalingMode.NearestNeighbor : BitmapScalingMode.HighQuality);
        zoomText.Text = $"{zoom * 100:0}%";
    }

    void FitZoom()
    {
        double vw = Math.Max(50, scroller.ViewportWidth) * dpiScale - 40, vh = Math.Max(50, scroller.ViewportHeight) * dpiScale - 40;
        zoom = Math.Min(vw / surface.CanvasSize.Width, vh / surface.CanvasSize.Height);
        ApplyZoom();
    }

    void FitIfLarge()
    {
        double vw = Math.Max(50, scroller.ViewportWidth) * dpiScale, vh = Math.Max(50, scroller.ViewportHeight) * dpiScale;
        if (surface.CanvasSize.Width > vw || surface.CanvasSize.Height > vh) FitZoom(); else ApplyZoom();
    }

    void OnWheel(object sender, MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
        e.Handled = true;
        var pImg = e.GetPosition(surface);
        var pView = e.GetPosition(scroller);
        zoom *= Math.Pow(1.1, e.Delta / 120.0);
        ApplyZoom();
        scroller.UpdateLayout();
        var pNew = surface.TranslatePoint(pImg, scroller);
        scroller.ScrollToHorizontalOffset(scroller.HorizontalOffset + pNew.X - pView.X);
        scroller.ScrollToVerticalOffset(scroller.VerticalOffset + pNew.Y - pView.Y);
    }

    // =====================================================================
    //  Tools / attributes
    // =====================================================================

    void SelectTool(Tool t)
    {
        if (textBox != null) CommitText();
        tool = t;
        foreach (var (k, b) in railButtons) b.IsChecked = k == t;
        ocrToggle.IsChecked = t == Tool.Ocr;
        surface.Cursor = t == Tool.Select ? Cursors.Arrow : Cursors.Cross;
        if (t != Tool.Crop) { surface.CropRect = null; surface.InvalidateVisual(); }
        RefreshContext();
        if (t == Tool.Picker) Status("Color picker: move over the image, click to copy the colour");
        else ShowDefaultStatus();
    }

    void PickColor(Color c)
    {
        color = c;
        SetSwatch(c);
        bool eraseCtx = tool == Tool.Blur && blurMode == 1;
        if (eraseCtx) eraseColorChosen = true;
        AttrChanged(a => { a.Color = c; if (a is BlurAnn b && b.FillMode == 1) b.UseColor = true; });
        if (eraseCtx) RefreshContext();
    }

    void PickCustomColor()
    {
        using var dlg = new System.Windows.Forms.ColorDialog { FullOpen = true, Color = System.Drawing.Color.FromArgb(color.R, color.G, color.B) };
        if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
        PickColor(Color.FromRgb(dlg.Color.R, dlg.Color.G, dlg.Color.B));
    }

    void SetSwatch(Color c)
    {
        bool match = false;
        foreach (var (col, ring) in swatchRings)
        {
            bool on = col == c;
            ring.Visibility = on ? Visibility.Visible : Visibility.Hidden;
            match |= on;
        }
        if (customRing != null) customRing.Visibility = match ? Visibility.Hidden : Visibility.Visible;
    }

    void AttrChanged(Action<Ann> apply)
    {
        if (surface.Selected == null) return;
        long now = Environment.TickCount64;
        if (now - lastAttrUndo > 700) PushUndo();
        lastAttrUndo = now;
        apply(surface.Selected);
        surface.InvalidateVisual();
    }

    void DeleteSelected()
    {
        if (surface.Selected == null) return;
        PushUndo();
        surface.Objects.Remove(surface.Selected);
        surface.Selected = null;
        surface.InvalidateVisual();
        RefreshContext();
        ShowDefaultStatus();
    }

    // =====================================================================
    //  Undo / redo
    // =====================================================================

    Snapshot Snap() => new(surface.Base, surface.Objects.Select(o => o.Clone()).ToList());

    void UpdateUndoUi()
    {
        undoBtn.IsEnabled = undo.Count > 0;
        redoBtn.IsEnabled = redo.Count > 0;
    }

    void PushUndo()
    {
        undo.Add(Snap());
        if (undo.Count > 100) undo.RemoveAt(0);
        redo.Clear();
        UpdateUndoUi();
    }

    void Restore(Snapshot s)
    {
        surface.Selected = null;
        surface.CropRect = null;
        surface.Objects = s.Objects;
        if (!ReferenceEquals(s.Base, surface.Base)) { surface.SetBase(s.Base); uncropFull = null; UpdateTitle(); }
        surface.InvalidateVisual();
        UpdateUndoUi();
        RefreshContext();
        ShowDefaultStatus();
    }

    void Undo()
    {
        if (textBox != null) return;
        if (undo.Count == 0) return;
        redo.Add(Snap());
        var s = undo[^1]; undo.RemoveAt(undo.Count - 1);
        Restore(s);
    }

    void Redo()
    {
        if (textBox != null) return;
        if (redo.Count == 0) return;
        undo.Add(Snap());
        var s = redo[^1]; redo.RemoveAt(redo.Count - 1);
        Restore(s);
    }

    // =====================================================================
    //  Mouse
    // =====================================================================

    Point ToImage(Point p) => new(p.X - surface.Origin.X, p.Y - surface.Origin.Y);

    Point Clamp(Point p) => new(Math.Clamp(p.X, 0, surface.Base.PixelWidth), Math.Clamp(p.Y, 0, surface.Base.PixelHeight));

    Ann? HitObject(Point p)
    {
        double tol = 4 / zoom;
        for (int i = surface.Objects.Count - 1; i >= 0; i--)
            if (surface.Objects[i].Hit(p, tol)) return surface.Objects[i];
        return null;
    }

    void OnDown(object sender, MouseButtonEventArgs e)
    {
        if (textBox != null) { CommitText(); }
        Keyboard.Focus(this);
        var p = Clamp(ToImage(e.GetPosition(surface)));
        startPt = lastPt = p;
        movePushed = false;

        if (tool == Tool.Picker) { PickAt(p); return; }

        // resize handles of the current selection take priority
        if (surface.Selected is { } sel)
        {
            var hp = sel.HandlePoints;
            for (int i = 0; i < hp.Length; i++)
                if ((hp[i] - p).Length <= 9 / zoom)
                {
                    drag = Drag.Handle; handleIdx = i; PushUndo(); surface.CaptureMouse(); return;
                }
        }

        var hit = HitObject(p);
        if (tool == Tool.Select || (hit is TextAnn && tool == Tool.Text))
        {
            surface.Selected = hit;
            surface.InvalidateVisual();
            RefreshContext();
            if (hit is TextAnn ta && e.ClickCount == 2) { BeginText(ta.Pos, ta); return; }
            if (hit is TextAnn ta2 && tool == Tool.Text) { BeginText(ta2.Pos, ta2); return; }
            if (hit != null) { drag = Drag.Move; surface.CaptureMouse(); }
            return;
        }

        switch (tool)
        {
            case Tool.Crop:
            case Tool.Ocr:
                surface.CropRect = new Rect(p, p);
                drag = Drag.Crop;
                surface.CaptureMouse();
                break;
            case Tool.Text:
                BeginText(p, null);
                break;
            case Tool.Counter:
                PushUndo();
                var n = surface.Objects.OfType<CounterAnn>().Select(c => c.Number).DefaultIfEmpty(0).Max() + 1;
                var ca = new CounterAnn { Center = p, Number = n, Color = color, Size = sizeSlider.Value };
                surface.Objects.Add(ca);
                surface.Selected = ca;
                surface.InvalidateVisual();
                UpdateCounterText();
                ShowDefaultStatus();
                break;
            default:
                PushUndo();
                Ann obj = tool switch
                {
                    Tool.Arrow => new ArrowAnn { P1 = p, P2 = p },
                    Tool.Line => new LineAnn { P1 = p, P2 = p },
                    Tool.Rect => new RectAnn { P1 = p, P2 = p },
                    Tool.Oval => new OvalAnn { P1 = p, P2 = p },
                    Tool.Blur => new BlurAnn { P1 = p, P2 = p },
                    Tool.Spot => new SpotlightAnn { P1 = p, P2 = p },
                    Tool.Ruler => new RulerAnn { P1 = p, P2 = p, Axis = rulerAxis },
                    Tool.Magnifier => new MagnifierAnn { Center = p, Radius = 1 },
                    Tool.Highlight => new FreeAnn { Highlighter = true, Points = { p } },
                    _ => new FreeAnn { Points = { p } },
                };
                obj.Color = color;
                obj.Size = sizeSlider.Value;
                if (obj.SupportsFill) obj.FillMode = obj is BlurAnn ? blurMode : fillMode;
                if (obj is BlurAnn nb) nb.UseColor = blurMode == 1 && eraseColorChosen;
                surface.Objects.Add(obj);
                surface.Selected = obj;
                drag = Drag.Create;
                surface.CaptureMouse();
                surface.InvalidateVisual();
                break;
        }
    }

    void OnMove(object sender, MouseEventArgs e)
    {
        var p = Clamp(ToImage(e.GetPosition(surface)));
        if (drag == Drag.None)
        {
            if (tool == Tool.Picker) { ShowPickerStatus(p); return; }
            if (statusTimer.IsEnabled) return; // leave a recent message visible
            string dims = $"{surface.Base.PixelWidth} × {surface.Base.PixelHeight} px";
            statusIcon!.Visibility = Visibility.Collapsed;
            statusText.Foreground = Ui.B("Text2Brush");
            statusText.Text = $"{(int)p.X}, {(int)p.Y}   •   {dims}";
            return;
        }
        bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
        switch (drag)
        {
            case Drag.Create when surface.Selected is RulerAnn ru:
                ru.P2 = p;
                break;
            case Drag.Create when surface.Selected is MagnifierAnn ma:
                ma.Radius = Math.Max(1, (p - ma.Center).Length);
                break;
            case Drag.Create when surface.Selected is TwoPointAnn tp:
                tp.P2 = shift ? Constrain(tp, p) : p;
                break;
            case Drag.Create when surface.Selected is FreeAnn fa:
                if ((p - fa.Points[^1]).Length >= 1.5) fa.Points.Add(p);
                break;
            case Drag.Move when surface.Selected is { } m:
                if (!movePushed) { PushUndo(); movePushed = true; }
                m.Move(p - lastPt);
                break;
            case Drag.Handle when surface.Selected is { } h:
                h.SetHandle(handleIdx, shift && h is TwoPointAnn t2 && h is not RulerAnn ? ConstrainHandle(t2, handleIdx, p) : p);
                break;
            case Drag.Crop:
                surface.CropRect = new Rect(startPt, p);
                UpdateCropText();
                if (tool == Tool.Ocr) Status($"{(int)surface.CropRect.Value.Width} × {(int)surface.CropRect.Value.Height} px — release to recognize");
                break;
        }
        lastPt = p;
        surface.InvalidateVisual();
    }

    static Point Constrain(TwoPointAnn a, Point p) => ConstrainHandle(a, 1, p);

    static Point ConstrainHandle(TwoPointAnn a, int idx, Point p)
    {
        var anchor = idx == 0 ? a.P2 : a.P1;
        var d = p - anchor;
        if (a is RectAnn || a is OvalAnn || a is BlurAnn || a is SpotlightAnn)
        {
            double m = Math.Max(Math.Abs(d.X), Math.Abs(d.Y));
            return new Point(anchor.X + Math.Sign(d.X == 0 ? 1 : d.X) * m, anchor.Y + Math.Sign(d.Y == 0 ? 1 : d.Y) * m);
        }
        double ang = Math.Round(Math.Atan2(d.Y, d.X) / (Math.PI / 4)) * (Math.PI / 4);
        double len = d.Length;
        return new Point(anchor.X + Math.Cos(ang) * len, anchor.Y + Math.Sin(ang) * len);
    }

    void OnUp(object sender, MouseButtonEventArgs e)
    {
        if (drag == Drag.None) return;
        surface.ReleaseMouseCapture();
        if (drag == Drag.Create)
        {
            bool tiny = surface.Selected switch
            {
                TwoPointAnn tp => Math.Abs(tp.P2.X - tp.P1.X) < 3 && Math.Abs(tp.P2.Y - tp.P1.Y) < 3,
                MagnifierAnn m => m.Radius < 6,
                _ => false,
            };
            // a stray click should not leave an invisible/zero-size object behind
            if (tiny)
            {
                surface.Objects.Remove(surface.Selected!);
                surface.Selected = null;
                if (undo.Count > 0) undo.RemoveAt(undo.Count - 1);
                UpdateUndoUi();
            }
        }
        bool ocr = drag == Drag.Crop && tool == Tool.Ocr;
        drag = Drag.None;
        surface.InvalidateVisual();
        RefreshContext();
        ShowDefaultStatus();
        if (ocr) RunOcrOnSelection();
    }

    async void RunOcrOnSelection()
    {
        var r = surface.CropRect ?? Rect.Empty;
        surface.CropRect = null;
        surface.InvalidateVisual();
        int bw = surface.Base.PixelWidth, bh = surface.Base.PixelHeight;
        int x = (int)r.X, y = (int)r.Y, w = (int)r.Width, h = (int)r.Height;
        BitmapSource src = surface.Base;
        if (!r.IsEmpty && w >= 4 && h >= 4)
        {
            w = Math.Min(w, bw - x); h = Math.Min(h, bh - y);
            var crop = new CroppedBitmap(surface.Base, new Int32Rect(x, y, w, h));
            crop.Freeze();
            src = crop;
        }
        Status("Recognizing text…");
        var outcome = await Ocr.RecognizeAsync(src);
        var msg = Ocr.ApplyToClipboard(outcome);
        Status(msg, outcome.Kind is "qr" or "text");
    }

    // =====================================================================
    //  Colour picker
    // =====================================================================

    bool EnsurePixels()
    {
        if (!ReferenceEquals(pixSrc, surface.Base))
        {
            var conv = new FormatConvertedBitmap(surface.Base, PixelFormats.Bgra32, null, 0);
            pixStride = conv.PixelWidth * 4;
            pix = new byte[pixStride * conv.PixelHeight];
            conv.CopyPixels(pix, pixStride, 0);
            pixSrc = surface.Base;
        }
        return pix != null;
    }

    Color? SampleAt(Point p)
    {
        if (!EnsurePixels()) return null;
        int x = Math.Clamp((int)p.X, 0, surface.Base.PixelWidth - 1), y = Math.Clamp((int)p.Y, 0, surface.Base.PixelHeight - 1);
        int i = y * pixStride + x * 4;
        return Color.FromRgb(pix![i + 2], pix[i + 1], pix[i]);
    }

    public static string FormatColor(Color c, string fmt)
    {
        switch (fmt)
        {
            case "rgb": return $"rgb({c.R}, {c.G}, {c.B})";
            case "hsl":
                double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
                double mx = Math.Max(r, Math.Max(g, b)), mn = Math.Min(r, Math.Min(g, b)), l = (mx + mn) / 2, h = 0, s = 0;
                if (mx != mn)
                {
                    double d = mx - mn;
                    s = l > 0.5 ? d / (2 - mx - mn) : d / (mx + mn);
                    h = mx == r ? (g - b) / d + (g < b ? 6 : 0) : mx == g ? (b - r) / d + 2 : (r - g) / d + 4;
                    h *= 60;
                }
                return $"hsl({Math.Round(h)}, {Math.Round(s * 100)}%, {Math.Round(l * 100)}%)";
            default: return $"#{c.R:X2}{c.G:X2}{c.B:X2}";
        }
    }

    void ShowPickerStatus(Point p)
    {
        if (SampleAt(p) is not { } c) return;
        pickSwatch.Background = new SolidColorBrush(c);
        pickText.Text = FormatColor(c, Settings.Current.ColorFormat);
        statusIcon!.Visibility = Visibility.Collapsed;
        statusSwatch.Background = new SolidColorBrush(c);
        statusSwatch.Visibility = Visibility.Visible;
        statusText.Text = $"{FormatColor(c, Settings.Current.ColorFormat)}   ({(int)p.X}, {(int)p.Y})   click to copy";
    }

    void PickAt(Point p)
    {
        if (SampleAt(p) is not { } c) return;
        var text = FormatColor(c, Settings.Current.ColorFormat);
        bool ok = Output.CopyText(text);
        if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) { color = c; SetSwatch(c); }
        pickSwatch.Background = new SolidColorBrush(c);
        pickText.Text = text;
        Status(ok ? $"Copied {text}" : "Can't copy the colour", ok);
        statusSwatch.Background = new SolidColorBrush(c);
        statusSwatch.Visibility = Visibility.Visible;
    }

    // =====================================================================
    //  Text
    // =====================================================================

    void BeginText(Point p, TextAnn? existing)
    {
        editingText = existing;
        double fs = existing?.FontSize ?? (10 + sizeSlider.Value * 1.8);
        var col = existing?.Color ?? color;
        if (existing != null) existing.Hidden = true;
        surface.InvalidateVisual();
        textBox = new TextBox
        {
            Text = existing?.Text ?? "",
            FontFamily = new FontFamily("Segoe UI"),
            FontWeight = FontWeights.SemiBold,
            FontSize = fs,
            Foreground = new SolidColorBrush(col),
            Background = new SolidColorBrush(Color.FromArgb(120, 0, 0, 0)),
            BorderBrush = Ui.B("AccentBrush"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(0),
            MinWidth = 60, MinHeight = 0,
            AcceptsReturn = true,
            CaretBrush = Brushes.White,
        };
        Canvas.SetLeft(textBox, p.X - 1);
        Canvas.SetTop(textBox, p.Y - 1);
        textBox.PreviewKeyDown += (_, ke) =>
        {
            if (ke.Key == Key.Escape) { ke.Handled = true; CancelText(); }
            else if (ke.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift) == 0) { ke.Handled = true; CommitText(); }
        };
        overlay.Children.Add(textBox);
        textBox.Dispatcher.BeginInvoke(new Action(() => { textBox?.Focus(); textBox?.SelectAll(); }), DispatcherPriority.Input);
    }

    void CommitText()
    {
        if (textBox == null) return;
        var tb = textBox; textBox = null;
        var text = tb.Text.TrimEnd();
        var pos = new Point(Canvas.GetLeft(tb) + 1, Canvas.GetTop(tb) + 1);
        overlay.Children.Remove(tb);
        var ed = editingText; editingText = null;
        if (ed != null)
        {
            ed.Hidden = false;
            if (text.Length == 0) { PushUndo(); surface.Objects.Remove(ed); surface.Selected = null; }
            else if (text != ed.Text) { PushUndo(); ed.Text = text; }
        }
        else if (text.Length > 0)
        {
            PushUndo();
            var ta = new TextAnn { Pos = pos, Text = text, Color = color, Size = sizeSlider.Value, FillMode = fillMode };
            surface.Objects.Add(ta);
            surface.Selected = ta;
        }
        surface.InvalidateVisual();
        Keyboard.Focus(this);
        ShowDefaultStatus();
    }

    void CancelText()
    {
        if (textBox == null) return;
        overlay.Children.Remove(textBox);
        textBox = null;
        if (editingText != null) editingText.Hidden = false;
        editingText = null;
        surface.InvalidateVisual();
        Keyboard.Focus(this);
    }

    // =====================================================================
    //  Keyboard
    // =====================================================================

    void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (textBox != null) return; // the text box handles its own keys
        if (Keyboard.FocusedElement is TextBox) return;
        var mods = Keyboard.Modifiers;
        bool ctrl = (mods & ModifierKeys.Control) != 0;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;

        if (ctrl)
        {
            switch (key)
            {
                case Key.Z: if ((mods & ModifierKeys.Shift) != 0) Redo(); else Undo(); e.Handled = true; return;
                case Key.Y: Redo(); e.Handled = true; return;
                case Key.C: CopyOut(); e.Handled = true; return;
                case Key.S: if ((mods & ModifierKeys.Shift) != 0) SaveAs(); else SaveOut(); e.Handled = true; return;
                case Key.P: PinIt(); e.Handled = true; return;
                case Key.R: ResizeImage(); e.Handled = true; return;
                case Key.D0: zoom = 1; ApplyZoom(); e.Handled = true; return;
                case Key.OemPlus: case Key.Add: zoom *= 1.25; ApplyZoom(); e.Handled = true; return;
                case Key.OemMinus: case Key.Subtract: zoom /= 1.25; ApplyZoom(); e.Handled = true; return;
            }
            return;
        }

        switch (key)
        {
            case Key.Escape:
                if (surface.CropRect != null) { surface.CropRect = null; surface.InvalidateVisual(); UpdateCropText(); }
                else FinishAndClose();
                e.Handled = true; return;
            case Key.Enter:
                if (surface.CropRect != null && tool == Tool.Crop) { ApplyCrop(); e.Handled = true; }
                return;
            case Key.Delete: case Key.Back:
                if (surface.Selected != null) { DeleteSelected(); e.Handled = true; }
                return;
            case Key.V: SelectTool(Tool.Select); break;
            case Key.A: SelectTool(Tool.Arrow); break;
            case Key.L: SelectTool(Tool.Line); break;
            case Key.R: SelectTool(Tool.Rect); break;
            case Key.O: SelectTool(Tool.Oval); break;
            case Key.T: SelectTool(Tool.Text); break;
            case Key.D: SelectTool(Tool.Free); break;
            case Key.H: SelectTool(Tool.Highlight); break;
            case Key.C: SelectTool(Tool.Counter); break;
            case Key.B: SelectTool(Tool.Blur); break;
            case Key.S: SelectTool(Tool.Spot); break;
            case Key.M: SelectTool(Tool.Magnifier); break;
            case Key.I: SelectTool(Tool.Picker); break;
            case Key.Q: SelectTool(Tool.Ocr); break;
            case Key.X: SelectTool(Tool.Crop); break;
            case Key.U: Uncrop(); break;
            case Key.K: backdropToggle.IsChecked = !(backdropToggle.IsChecked == true); SetBackdrop(backdropToggle.IsChecked == true); break;
            case Key.D1: case Key.NumPad1: rulerAxis = 1; SelectTool(Tool.Ruler); break;
            case Key.D2: case Key.NumPad2: rulerAxis = 2; SelectTool(Tool.Ruler); break;
            default: return;
        }
        e.Handled = true;
    }

    // =====================================================================
    //  Crop / uncrop / resize
    // =====================================================================

    void ApplyCrop()
    {
        if (surface.CropRect is not { } cr) return;
        int x = (int)Math.Round(cr.X), y = (int)Math.Round(cr.Y);
        int w = (int)Math.Round(cr.Width), h = (int)Math.Round(cr.Height);
        w = Math.Min(w, surface.Base.PixelWidth - x);
        h = Math.Min(h, surface.Base.PixelHeight - y);
        if (w < 2 || h < 2) { surface.CropRect = null; surface.InvalidateVisual(); return; }
        PushUndo();
        var nb = new WriteableBitmap(new CroppedBitmap(surface.Base, new Int32Rect(x, y, w, h)));
        nb.Freeze();
        if (uncropFull == null) { uncropFull = surface.Base; uncropX = x; uncropY = y; }
        else { uncropX += x; uncropY += y; }
        foreach (var o in surface.Objects) o.Move(new Vector(-x, -y));
        surface.CropRect = null;
        surface.Selected = null;
        surface.SetBase(nb);
        UpdateTitle();
        SelectTool(Tool.Crop); // stay in the crop context so Uncrop is available
        Status($"Cropped to {w} × {h} px", true);
    }

    void Uncrop()
    {
        if (textBox != null) return;
        if (uncropFull == null) { Status("No image to uncrop"); return; }
        PushUndo();
        foreach (var o in surface.Objects) o.Move(new Vector(uncropX, uncropY));
        surface.Selected = null;
        surface.SetBase(uncropFull);
        uncropFull = null;
        surface.InvalidateVisual();
        UpdateTitle();
        RefreshContext();
        Status("Crop undone", true);
    }

    void ResizeImage()
    {
        if (textBox != null) CommitText();
        var dlg = new ResizeDialog(this, surface.Base.PixelWidth, surface.Base.PixelHeight);
        if (dlg.ShowDialog() != true) return;
        int nw = dlg.NewWidth, nh = dlg.NewHeight;
        if (nw == surface.Base.PixelWidth && nh == surface.Base.PixelHeight) return;
        if ((long)nw * nh > 60_000_000) { Status("Crop or resize to a smaller size first"); return; }

        var flat = surface.Render(false);
        var dv = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(dv, BitmapScalingMode.HighQuality);
        using (var dc = dv.RenderOpen()) dc.DrawImage(flat, new Rect(0, 0, nw, nh));
        var rtb = new RenderTargetBitmap(nw, nh, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        rtb.Freeze();

        PushUndo();
        surface.Objects.Clear();
        surface.Selected = null;
        surface.SetBase(rtb);
        uncropFull = null;
        UpdateTitle();
        FitIfLarge();
        RefreshContext();
        Status($"Resized to {nw} × {nh} px", true);
    }

    // =====================================================================
    //  Backdrop
    // =====================================================================

    void SetBackdrop(bool on)
    {
        bd.Enabled = on;
        backdropToggle.IsChecked = on;
        if (on) { RebuildBackdropPanel(); }
        backdropHost.Visibility = on && !backdropInPopup ? Visibility.Visible : Visibility.Collapsed;
        backdropPopup.IsOpen = on && backdropInPopup;
        BackdropChanged();
        Dispatcher.BeginInvoke(new Action(FitIfLarge), DispatcherPriority.Loaded);
    }

    void PlaceBackdropPanel()
    {
        if (backdropPanel == null) return;
        // detach the panel from wherever it currently lives before re-parenting it
        if (backdropPopup.Child is Border oldCard) oldCard.Child = null;
        backdropHost.Child = null;
        backdropPopup.Child = null;
        if (backdropInPopup)
        {
            backdropPopup.Child = new Border
            {
                Background = Ui.B("Bg2Brush"), BorderBrush = Ui.B("Line2Brush"), BorderThickness = new Thickness(1), CornerRadius = Ui.Radius("RadiusLg"),
                Width = 300, Height = 560, Margin = new Thickness(0, 0, 32, 32), Effect = (System.Windows.Media.Effects.Effect)FindResource("PopoverShadow"), Child = backdropPanel,
            };
            backdropHost.Visibility = Visibility.Collapsed;
            backdropPopup.IsOpen = bd.Enabled;
        }
        else
        {
            backdropHost.Child = backdropPanel;
            backdropPopup.IsOpen = false;
            backdropHost.Visibility = bd.Enabled ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    void BackdropChanged()
    {
        surface.Backdrop = bd;
        surface.RefreshLayout();
        var keep = bd.Clone(); keep.Enabled = false;
        Settings.Current.Backdrop = keep;
        Settings.Current.Save();
        ShowDefaultStatus();
    }

    static TextBlock SectionLabel(string text) => new()
    {
        Text = text.ToUpperInvariant(), FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = Ui.B("Text3Brush"), Margin = new Thickness(0, 18, 0, 10),
    };

    static Border ValueBox(TextBlock tb) => new()
    {
        Background = Ui.B("Bg0Brush"), BorderBrush = Ui.B("Line2Brush"), BorderThickness = new Thickness(1), CornerRadius = Ui.Radius("RadiusSm"),
        Width = 42, Height = 28, Margin = new Thickness(10, 0, 0, 0), Child = tb,
    };

    FrameworkElement SliderRow(string label, double min, double max, double val, Action<double> set, string? tip = null)
    {
        var g = new Grid { Margin = new Thickness(0, 0, 0, 10), ToolTip = tip };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(104) });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.Children.Add(new TextBlock { Text = label, Foreground = Ui.B("Text2Brush"), VerticalAlignment = VerticalAlignment.Center });
        var vt = new TextBlock { Text = ((int)val).ToString(), Foreground = Ui.B("Text2Brush"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var s = new Slider { Minimum = min, Maximum = max, Value = val, VerticalAlignment = VerticalAlignment.Center };
        s.ValueChanged += (_, _) => { vt.Text = ((int)s.Value).ToString(); set(s.Value); BackdropChanged(); };
        Grid.SetColumn(s, 1);
        g.Children.Add(s);
        var box = ValueBox(vt);
        Grid.SetColumn(box, 2);
        g.Children.Add(box);
        return g;
    }

    FrameworkElement ColorButton(string label, Func<string> get, Action<string> set)
    {
        var c = BackdropRenderer.Parse(get());
        var b = new Button { Padding = new Thickness(10, 0, 10, 0), Height = 36, HorizontalContentAlignment = HorizontalAlignment.Stretch, ToolTip = "Pick a custom colour" };
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.Children.Add(new Ellipse { Width = 18, Height = 18, Fill = new SolidColorBrush(c), Stroke = Ui.B("Line3Brush"), StrokeThickness = 1, Margin = new Thickness(0, 0, 8, 0) });
        var l = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(l, 1); g.Children.Add(l);
        var hx = new TextBlock { Text = get().TrimStart('#').ToUpperInvariant(), Foreground = Ui.B("Text3Brush"), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, FontFamily = new FontFamily("Cascadia Mono, Consolas"), FontSize = 11.5 };
        Grid.SetColumn(hx, 2); g.Children.Add(hx);
        b.Content = g;
        b.Click += (_, _) =>
        {
            var cur = BackdropRenderer.Parse(get());
            using var dlg = new System.Windows.Forms.ColorDialog { FullOpen = true, Color = System.Drawing.Color.FromArgb(cur.R, cur.G, cur.B) };
            if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
            set(BackdropRenderer.ToHex(Color.FromRgb(dlg.Color.R, dlg.Color.G, dlg.Color.B)));
            if (bd.Mode is "mix" or "image") bd.Mode = "gradient";
            BackdropChanged(); RebuildBackdropPanel();
        };
        return b;
    }

    void RebuildBackdropPanel()
    {
        var stack = new StackPanel { Margin = new Thickness(18, 0, 18, 12) };

        // background type
        stack.Children.Add(SectionLabel("Background"));
        var modeGrid = new UniformGrid { Columns = 4 };
        foreach (var m in new[] { "Gradient", "Solid", "Image", "Mix" })
        {
            var rb = new RadioButton { Style = StyleOf("SegmentItem"), GroupName = "bdmode", Content = m, IsChecked = bd.Mode == m.ToLowerInvariant(), Padding = new Thickness(0, 6, 0, 6) };
            var mm = m.ToLowerInvariant();
            rb.Checked += (_, _) => { if (bd.Mode == mm) return; bd.Mode = mm; BackdropChanged(); RebuildBackdropPanel(); };
            modeGrid.Children.Add(rb);
        }
        stack.Children.Add(new Border { Style = StyleOf("SegmentBox"), Child = modeGrid, Margin = new Thickness(0, 0, 0, 12) });

        // presets
        if (bd.Mode is "gradient" or "solid")
        {
            var presets = new UniformGrid { Columns = 4 };
            foreach (var (from, to, name) in BackdropRenderer.Presets)
            {
                bool on = bd.Color1.Equals(from, StringComparison.OrdinalIgnoreCase) && bd.Color2.Equals(to, StringComparison.OrdinalIgnoreCase);
                var card = new StackPanel { Margin = new Thickness(0, 0, 8, 10), Cursor = Cursors.Hand, Background = Brushes.Transparent };
                card.Children.Add(new Border
                {
                    Height = 44, CornerRadius = new CornerRadius(7), BorderThickness = new Thickness(on ? 2 : 1), BorderBrush = Ui.B(on ? "AccentBrush" : "Line2Brush"),
                    Background = new LinearGradientBrush(BackdropRenderer.Parse(from), BackdropRenderer.Parse(to), 45),
                });
                card.Children.Add(new TextBlock { Text = name, FontSize = 11, Foreground = Ui.B(on ? "AccentTextBrush" : "Text3Brush"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0) });
                var f = from; var t = to;
                card.MouseLeftButtonDown += (_, _) => { bd.Color1 = f; bd.Color2 = t; bd.Mode = "gradient"; BackdropChanged(); RebuildBackdropPanel(); };
                presets.Children.Add(card);
            }
            stack.Children.Add(presets);

            var colors = new Grid { Margin = new Thickness(0, 2, 0, 12) };
            colors.ColumnDefinitions.Add(new ColumnDefinition());
            colors.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            colors.ColumnDefinitions.Add(new ColumnDefinition());
            colors.Children.Add(ColorButton(bd.Mode == "solid" ? "Color" : "From", () => bd.Color1, v => bd.Color1 = v));
            if (bd.Mode == "gradient")
            {
                var to = ColorButton("To", () => bd.Color2, v => bd.Color2 = v);
                Grid.SetColumn(to, 2);
                colors.Children.Add(to);
            }
            stack.Children.Add(colors);

            if (bd.Mode == "gradient")
            {
                var row = new Grid { Margin = new Thickness(0, 0, 0, 4) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition());
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var seg = new StackPanel { Orientation = Orientation.Horizontal };
                foreach (var gt in new[] { "Linear", "Radial" })
                {
                    var rb = new RadioButton { Style = StyleOf("SegmentItem"), GroupName = "bdtype", Content = gt, IsChecked = bd.GradientType == gt.ToLowerInvariant() };
                    var gg = gt.ToLowerInvariant();
                    rb.Checked += (_, _) => { if (bd.GradientType == gg) return; bd.GradientType = gg; BackdropChanged(); };
                    seg.Children.Add(rb);
                }
                row.Children.Add(new Border { Style = StyleOf("SegmentBox"), Child = seg });
                var av = new TextBlock { Text = bd.Angle.ToString(), Foreground = Ui.B("Text2Brush"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                var s = new Slider { Minimum = 0, Maximum = 360, Value = bd.Angle, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, ToolTip = "Gradient angle" };
                s.ValueChanged += (_, _) => { av.Text = ((int)s.Value).ToString(); bd.Angle = (int)s.Value; BackdropChanged(); };
                Grid.SetColumn(s, 1); row.Children.Add(s);
                var box = ValueBox(av); Grid.SetColumn(box, 2); row.Children.Add(box);
                var dice = IconBtn("Dice", "Random gradient", () => { BackdropRenderer.Randomized(bd); BackdropChanged(); RebuildBackdropPanel(); });
                dice.Margin = new Thickness(6, 0, 0, 0);
                Grid.SetColumn(dice, 3); row.Children.Add(dice);
                stack.Children.Add(row);
            }
        }
        else if (bd.Mode == "image")
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            var pick = new Button { Height = 36, Content = Ui.IconLabel("Image", "Choose image…", 16, 8), ToolTip = "Use your own picture as the background" };
            pick.Click += (_, _) =>
            {
                var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif|All files|*.*" };
                if (dlg.ShowDialog(this) != true) return;
                bd.ImagePath = dlg.FileName; bd.UseWallpaper = false; bd.Mode = "image";
                BackdropChanged(); RebuildBackdropPanel();
            };
            var wall = new Button { Height = 36, Content = Ui.IconLabel("Monitor", "Wallpaper", 16, 8), ToolTip = "Use your current desktop wallpaper" };
            wall.Click += (_, _) => { bd.UseWallpaper = true; bd.ImagePath = ""; bd.Mode = "image"; BackdropChanged(); RebuildBackdropPanel(); };
            row.Children.Add(pick);
            Grid.SetColumn(wall, 2);
            row.Children.Add(wall);
            stack.Children.Add(row);
            var src = bd.UseWallpaper ? "Desktop wallpaper" : string.IsNullOrEmpty(bd.ImagePath) ? "No image chosen — using the gradient" : System.IO.Path.GetFileName(bd.ImagePath);
            stack.Children.Add(new TextBlock { Text = src, Foreground = Ui.B("Text3Brush"), FontSize = 12, Margin = new Thickness(0, 0, 0, 10), TextTrimming = TextTrimming.CharacterEllipsis });
            stack.Children.Add(SliderRow("Blur", 0, 60, bd.Blur, v => bd.Blur = (int)v, "Blur the picture"));
        }
        else // mix
        {
            stack.Children.Add(new TextBlock { Text = "Blends the screenshot's own colours into a soft background.", Foreground = Ui.B("Text3Brush"), FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) });
        }

        // frame
        stack.Children.Add(SectionLabel("Frame"));
        stack.Children.Add(SliderRow("Padding", 0, 300, bd.Padding, v => bd.Padding = (int)v));
        stack.Children.Add(SliderRow("Corner radius", 0, 60, bd.InnerRadius, v => bd.InnerRadius = (int)v, "Screenshot corner radius"));
        stack.Children.Add(SliderRow("Canvas radius", 0, 120, bd.OuterRadius, v => bd.OuterRadius = (int)v, "Canvas corner radius"));
        stack.Children.Add(SliderRow("Shadow", 0, 100, bd.Shadow, v => bd.Shadow = (int)v));

        // canvas size
        stack.Children.Add(SectionLabel("Canvas size"));
        var pills = new WrapPanel();
        foreach (var a in new[] { "auto", "16:9", "4:3", "3:2", "1:1", "4:5", "9:16" })
        {
            var tb = new ToggleButton { Style = StyleOf("PillToggle"), Content = a == "auto" ? "Auto" : a, IsChecked = bd.Aspect == a };
            var aa = a;
            tb.Click += (_, _) => { bd.Aspect = aa; BackdropChanged(); RebuildBackdropPanel(); };
            pills.Children.Add(tb);
        }
        stack.Children.Add(pills);

        // header + footer
        var title = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        title.Children.Add(Ui.Icon("Backdrop", 18, Ui.B("AccentTextBrush")));
        title.Children.Add(new TextBlock { Text = "Backdrop", FontSize = 14, FontWeight = FontWeights.SemiBold, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });

        var enable = new CheckBox { Style = StyleOf("SwitchCheckBox"), IsChecked = bd.Enabled, ToolTip = "Apply the backdrop" };
        enable.Click += (_, _) => SetBackdrop(enable.IsChecked == true);
        var close = IconBtn("Close", "Close", () => SetBackdrop(false));
        var headRight = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        headRight.Children.Add(enable);
        headRight.Children.Add(close);
        close.Margin = new Thickness(10, 0, 0, 0);
        var head = new Grid { Height = 52, Margin = new Thickness(18, 0, 14, 0) };
        head.Children.Add(title);
        head.Children.Add(headRight);

        var reset = new Button { Style = StyleOf("GhostButton"), Content = Ui.IconLabel("Reset", "Reset", 15, 8), HorizontalAlignment = HorizontalAlignment.Left };
        reset.Click += (_, _) => { bd = new BackdropSettings { Enabled = bd.Enabled }; BackdropChanged(); RebuildBackdropPanel(); };
        var foot = new Grid { Height = 52, Margin = new Thickness(12, 0, 18, 0) };
        foot.Children.Add(reset);
        foot.Children.Add(new TextBlock { Text = "Saved with the image", Foreground = Ui.B("Text3Brush"), FontSize = 12, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center });

        var dock = new DockPanel();
        var headB = new Border { Child = head, BorderBrush = Ui.B("LineBrush"), BorderThickness = new Thickness(0, 0, 0, 1) };
        var footB = new Border { Child = foot, BorderBrush = Ui.B("LineBrush"), BorderThickness = new Thickness(0, 1, 0, 0) };
        DockPanel.SetDock(headB, Dock.Top);
        DockPanel.SetDock(footB, Dock.Bottom);
        dock.Children.Add(headB);
        dock.Children.Add(footB);
        dock.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = stack });
        backdropPanel = dock;
        PlaceBackdropPanel();
    }

    // =====================================================================
    //  Output
    // =====================================================================

    /// <summary>Press and drag to drop the (flattened) image as a PNG file / bitmap into another app.</summary>
    Button MakeDragOut()
    {
        var b = new Button { Style = StyleOf("IconButton"), ToolTip = RichTip("Drag out", null, "Drag the image into another app or folder"), Content = Ui.Icon("Drag", 18) };
        Point? down = null;
        b.PreviewMouseLeftButtonDown += (_, e) => down = e.GetPosition(b);
        b.PreviewMouseLeftButtonUp += (_, _) => down = null;
        b.PreviewMouseMove += (_, e) =>
        {
            if (e.LeftButton != MouseButtonState.Pressed || down == null) return;
            var d = e.GetPosition(b) - down.Value;
            if (Math.Abs(d.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(d.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            down = null;
            try
            {
                var img = Flatten();
                var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Blueprint");
                Directory.CreateDirectory(dir);
                var path = System.IO.Path.Combine(dir, "SCR-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".png");
                Output.Encode(img, path, false);
                var data = new DataObject();
                data.SetData(DataFormats.FileDrop, new[] { path });
                data.SetImage(new FormatConvertedBitmap(img, PixelFormats.Bgr32, null, 0));
                DragDrop.DoDragDrop(b, data, DragDropEffects.Copy);
            }
            catch (Exception ex) { Status("Can't drag the image out: " + ex.Message); }
        };
        return b;
    }

    BitmapSource Flatten()
    {
        if (textBox != null) CommitText();
        return surface.Render();
    }

    void CopyOut()
    {
        bool ok = Output.CopyToClipboard(Flatten());
        Status(ok ? "Copied to the clipboard" : "Can't copy image", ok);
    }

    void SaveOut()
    {
        var path = Output.SaveToFolder(Flatten(), out var err);
        if (path != null) Status("Saved to " + path, true); else Status("Can't save the image: " + err);
    }

    void SaveAs()
    {
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "PNG image|*.png|JPEG image|*.jpg",
            FileName = "SCR-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"),
            InitialDirectory = Directory.Exists(Settings.Current.Folder) ? Settings.Current.Folder : null,
        };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            Output.Encode(Flatten(), dlg.FileName, dlg.FileName.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase));
            Status("Saved to " + dlg.FileName, true);
        }
        catch (Exception ex) { Status("Failed to save file: " + ex.Message); }
    }

    void PinIt() => new PinWindow(Flatten()).Show();

    void FinishAndClose()
    {
        var s = Settings.Current;
        if (s.EscCopy || s.EscSave)
        {
            var img = Flatten();
            if (s.EscCopy) Output.CopyToClipboard(img);
            if (s.EscSave) Output.SaveToFolder(img, out _);
        }
        Close();
    }
}
