using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace Blueprint;

/// <summary>Frozen-screen area picker: one borderless topmost window per monitor, like the original RegionSelector.</summary>
static class RegionSelector
{
    static List<OverlayWindow>? active;

    /// <summary>Raised when the picker closes (picked or cancelled).</summary>
    public static event Action? Ended;

    public static void Start(Action<Drawing.Rectangle, BitmapSource> done, bool pickWindow = false)
    {
        if (active != null) { Ended?.Invoke(); return; }
        var wins = new List<OverlayWindow>();
        active = wins;
        try
        {
            foreach (var s in Forms.Screen.AllScreens)
                wins.Add(new OverlayWindow(s.Bounds, ScreenCapture.Grab(s.Bounds), pickWindow));
        }
        catch
        {
            // a failed grab (secure desktop, display change) must not leave the picker "busy" forever
            active = null;
            foreach (var w in wins) { try { w.Close(); } catch { } }
            Ended?.Invoke();
            throw;
        }

        void CloseAll()
        {
            var list = active;
            active = null;
            if (list != null) foreach (var w in list) w.Close();
            Ended?.Invoke();
        }

        foreach (var w in wins)
        {
            w.Picked += (rect, img) => { CloseAll(); done(rect, img); };
            w.Cancelled += CloseAll;
            w.Show();
        }
        var cur = Forms.Cursor.Position;
        (wins.FirstOrDefault(w => w.ScreenBounds.Contains(cur)) ?? wins[0]).Activate();
    }
}

sealed class OverlayWindow : Window
{
    public readonly Drawing.Rectangle ScreenBounds;
    public event Action<Drawing.Rectangle, BitmapSource>? Picked;
    public event Action? Cancelled;

    readonly BitmapSource img;
    readonly Canvas root = new();
    readonly Path dim = new() { Fill = Ui.B("DimOverlayBrush"), IsHitTestVisible = false };
    readonly Rectangle selBorder = new() { Stroke = Brushes.White, StrokeThickness = 1.5, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
    readonly Line[] guides = new Line[4];
    readonly Line crossH = new(), crossV = new();
    readonly Border sizePill = Pill();
    readonly TextBlock sizeText = new() { Foreground = Brushes.White, FontSize = 13, FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI") };
    readonly Border hint;
    readonly StackPanel hintRow = new() { Orientation = Orientation.Horizontal };
    readonly Border windowLabel = new() { Background = Ui.B("AccentBrush"), CornerRadius = new CornerRadius(14), Padding = new Thickness(12, 5, 14, 5), Visibility = Visibility.Collapsed, IsHitTestVisible = false };
    readonly TextBlock windowLabelText = new() { Foreground = Brushes.White, FontSize = 13, FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI") };
    readonly Border loupe = new() { Background = Ui.B("MenuBrush"), BorderBrush = Ui.B("Line3Brush"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(0), IsHitTestVisible = false, ClipToBounds = true };
    readonly Image loupeImg = new() { Width = 132, Height = 132, Stretch = Stretch.Fill };
    readonly TextBlock loupeText = new() { Foreground = Brushes.White, FontSize = 12, FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI") };
    readonly Border loupeSwatch = new() { Width = 11, Height = 11, CornerRadius = new CornerRadius(2), Margin = new Thickness(6, 0, 0, 0), BorderBrush = Brushes.White, BorderThickness = new Thickness(0.5) };

    bool dragging;
    Point start, lastMouse;
    Rect sel;
    bool fixedOnce;
    bool pickWindow; // "any window" mode: hover highlights a window, click captures it (Tab switches to area mode)
    bool hasPick;
    double scale = 1;

    public OverlayWindow(Drawing.Rectangle bounds, BitmapSource image, bool pickWindow = false)
    {
        ScreenBounds = bounds;
        img = image;
        this.pickWindow = pickWindow;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        Background = Brushes.Black;
        Cursor = Cursors.Cross;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = bounds.X; Top = bounds.Y; Width = 100; Height = 100;

        root.Width = bounds.Width; root.Height = bounds.Height;
        root.UseLayoutRounding = true;
        root.SnapsToDevicePixels = true;
        root.Background = Brushes.Transparent;

        var image2 = new Image { Source = img, Width = img.PixelWidth, Height = img.PixelHeight, Stretch = Stretch.None, IsHitTestVisible = false };
        RenderOptions.SetBitmapScalingMode(image2, BitmapScalingMode.NearestNeighbor);
        root.Children.Add(image2);
        root.Children.Add(dim);
        for (int i = 0; i < 4; i++)
        {
            guides[i] = MakeLine();
            guides[i].Visibility = Visibility.Collapsed;
            root.Children.Add(guides[i]);
        }
        foreach (var l in new[] { crossH, crossV }) { StyleLine(l); root.Children.Add(l); }
        root.Children.Add(selBorder);

        sizePill.Child = sizeText;
        windowLabel.Child = windowLabelText;
        hint = Pill();
        hint.Child = hintRow;
        // loupe: magnified pixels + caption
        var lstack = new StackPanel();
        var loupeGrid = new Grid();
        loupeGrid.Children.Add(loupeImg);
        loupeGrid.Children.Add(new Border { Width = 12, Height = 12, BorderBrush = Brushes.White, BorderThickness = new Thickness(1.5), Background = Brushes.Transparent, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 0), Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 0, ShadowDepth = 0, Color = Colors.Black, Opacity = 1 } });
        lstack.Children.Add(loupeGrid);
        var cap = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(10, 7, 10, 8) };
        cap.Children.Add(loupeText);
        cap.Children.Add(loupeSwatch);
        lstack.Children.Add(cap);
        loupe.Child = lstack;

        foreach (var e in new FrameworkElement[] { sizePill, windowLabel, hint, loupe })
        {
            e.RenderTransformOrigin = new Point(0, 0);
            e.Visibility = Visibility.Collapsed;
            root.Children.Add(e);
        }
        Content = root;
        UpdateDim();
        BuildHint();

        MouseMove += OnMove;
        MouseLeftButtonDown += OnDown;
        MouseLeftButtonUp += OnUp;
        MouseRightButtonDown += (_, _) => Cancelled?.Invoke();
        KeyDown += OnKey;
        ContentRendered += (_, _) => { if (!fixedOnce) { fixedOnce = true; Fix(); } };
    }

    static Border Pill() => new()
    {
        Background = new SolidColorBrush(Color.FromArgb(235, 28, 28, 32)), BorderBrush = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
        BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14), Padding = new Thickness(12, 5, 12, 5), IsHitTestVisible = false,
    };

    static Line MakeLine() { var l = new Line(); StyleLine(l); return l; }
    static void StyleLine(Line l)
    {
        l.Stroke = new SolidColorBrush(Color.FromArgb(200, 255, 255, 255));
        l.StrokeThickness = 1;
        l.StrokeDashArray = new DoubleCollection { 4, 4 };
        l.IsHitTestVisible = false;
    }

    // ---------- hint pill ----------

    void BuildHint()
    {
        hintRow.Children.Clear();
        TextBlock T(string s) => new() { Text = s, Foreground = new SolidColorBrush(Color.FromRgb(0xED, 0xED, 0xF0)), FontSize = 13, VerticalAlignment = VerticalAlignment.Center, FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI") };
        TextBlock Dot() => new() { Text = "•", Foreground = Ui.B("Text3Brush"), Margin = new Thickness(10, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
        var icon = Ui.Icon(pickWindow ? "Anywindow" : "Area", 17, Brushes.White);
        icon.Margin = new Thickness(0, 0, 9, 0); icon.VerticalAlignment = VerticalAlignment.Center;
        hintRow.Children.Add(icon);
        if (pickWindow)
        {
            hintRow.Children.Add(T("Click a window to capture")); hintRow.Children.Add(Dot());
            hintRow.Children.Add(Ui.Keycap("Tab")); hintRow.Children.Add(T("drag an area instead")); hintRow.Children.Add(Dot());
            hintRow.Children.Add(Ui.Keycap("Esc")); hintRow.Children.Add(T("cancel"));
        }
        else
        {
            hintRow.Children.Add(T("Drag to capture")); hintRow.Children.Add(Dot());
            hintRow.Children.Add(Ui.Keycap("Space")); hintRow.Children.Add(T("move")); hintRow.Children.Add(Dot());
            hintRow.Children.Add(Ui.Keycap("Esc")); hintRow.Children.Add(T("cancel"));
        }
        hint.Visibility = Visibility.Visible;
        PositionHint();
    }

    void PositionHint()
    {
        if (!IsLoaded && scale == 1 && !fixedOnce) { }
        hint.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        double w = hint.DesiredSize.Width * scale, h = hint.DesiredSize.Height * scale;
        Put(hint, (ScreenBounds.Width - w) / 2, pickWindow ? ScreenBounds.Height - h - 40 * scale : 36 * scale);
    }

    /// <summary>Position a chrome element in physical pixels and scale it to look right at the monitor's DPI.</summary>
    void Put(FrameworkElement el, double x, double y)
    {
        el.RenderTransform = new ScaleTransform(scale, scale);
        Canvas.SetLeft(el, x);
        Canvas.SetTop(el, y);
    }

    Size Dims(FrameworkElement el)
    {
        el.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return new Size(el.DesiredSize.Width * scale, el.DesiredSize.Height * scale);
    }

    // ---------- placement ----------

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        Dispatcher.BeginInvoke(new Action(Fix));
    }

    /// <summary>Place the window over the monitor in physical pixels and make 1 layout unit == 1 physical pixel.</summary>
    void Fix()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        Native.SetWindowPos(hwnd, Native.HWND_TOPMOST, ScreenBounds.X, ScreenBounds.Y, ScreenBounds.Width, ScreenBounds.Height, Native.SWP_SHOWWINDOW);
        scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        root.LayoutTransform = new ScaleTransform(1 / scale, 1 / scale);
        PositionHint();
        Focus();
    }

    void UpdateDim()
    {
        var full = new RectangleGeometry(new Rect(0, 0, ScreenBounds.Width, ScreenBounds.Height));
        dim.Data = (dragging || hasPick) && sel.Width > 0 && sel.Height > 0
            ? new CombinedGeometry(GeometryCombineMode.Exclude, full, new RectangleGeometry(sel, pickWindow ? 8 : 0, pickWindow ? 8 : 0))
            : full;
    }

    // ---------- pointer ----------

    void OnKey(object s, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) Cancelled?.Invoke();
        else if (e.Key == Key.Tab && !dragging)
        {
            e.Handled = true;
            pickWindow = !pickWindow;
            hasPick = false;
            ResetVisuals();
            BuildHint();
        }
    }

    void ResetVisuals()
    {
        selBorder.Visibility = Visibility.Collapsed;
        foreach (var g in guides) g.Visibility = Visibility.Collapsed;
        sizePill.Visibility = windowLabel.Visibility = Visibility.Collapsed;
        sel = Rect.Empty;
        UpdateDim();
    }

    void OnMove(object s, MouseEventArgs e)
    {
        var p = e.GetPosition(root);
        if (pickWindow)
        {
            crossH.Visibility = crossV.Visibility = Visibility.Collapsed;
            var hit = ScreenCapture.WindowAt(new Drawing.Point(ScreenBounds.X + (int)p.X, ScreenBounds.Y + (int)p.Y));
            var vis = hit is { } h ? Drawing.Rectangle.Intersect(h.rect, ScreenBounds) : Drawing.Rectangle.Empty;
            hasPick = !vis.IsEmpty;
            if (hasPick)
            {
                sel = new Rect(vis.X - ScreenBounds.X, vis.Y - ScreenBounds.Y, vis.Width, vis.Height);
                selBorder.Stroke = Ui.B("AccentBrush"); selBorder.StrokeThickness = 3; selBorder.RadiusX = selBorder.RadiusY = 8;
                ShowSelection();
                foreach (var g in guides) g.Visibility = Visibility.Collapsed;
                sizePill.Visibility = Visibility.Collapsed;
                windowLabelText.Text = (string.IsNullOrEmpty(hit!.Value.title) ? "Window" : hit.Value.title) + "   " + vis.Width + " × " + vis.Height;
                windowLabel.Visibility = Visibility.Visible;
                Put(windowLabel, sel.X + 18 * scale, sel.Y + 18 * scale);
            }
            else ResetVisuals();
            UpdateLoupe(p, false);
            lastMouse = p;
            return;
        }

        if (!dragging)
        {
            crossH.X1 = 0; crossH.X2 = ScreenBounds.Width; crossH.Y1 = crossH.Y2 = p.Y + 0.5;
            crossV.Y1 = 0; crossV.Y2 = ScreenBounds.Height; crossV.X1 = crossV.X2 = p.X + 0.5;
            crossH.Visibility = crossV.Visibility = Visibility.Visible;
            sizeText.Text = $"{ScreenBounds.X + (int)p.X}, {ScreenBounds.Y + (int)p.Y}";
            sizePill.Visibility = Visibility.Visible;
            var d = Dims(sizePill);
            Put(sizePill, Math.Min(p.X + 18 * scale, ScreenBounds.Width - d.Width - 4), Math.Min(p.Y + 18 * scale, ScreenBounds.Height - d.Height - 4));
            UpdateLoupe(p, true);
            lastMouse = p;
            return;
        }

        double x = Math.Clamp(p.X, 0, ScreenBounds.Width), y = Math.Clamp(p.Y, 0, ScreenBounds.Height);
        if (Keyboard.IsKeyDown(Key.Space))
        {
            // move the selection instead of resizing it
            var delta = p - lastMouse;
            double nx = Math.Clamp(sel.X + delta.X, 0, ScreenBounds.Width - sel.Width), ny = Math.Clamp(sel.Y + delta.Y, 0, ScreenBounds.Height - sel.Height);
            start = new Point(start.X + (nx - sel.X), start.Y + (ny - sel.Y));
            sel = new Rect(nx, ny, sel.Width, sel.Height);
        }
        else sel = new Rect(Math.Min(start.X, x), Math.Min(start.Y, y), Math.Abs(x - start.X), Math.Abs(y - start.Y));
        lastMouse = p;
        UpdateSelectionVisuals();
        UpdateLoupe(p, true);
    }

    void ShowSelection()
    {
        selBorder.Visibility = Visibility.Visible;
        Canvas.SetLeft(selBorder, sel.X); Canvas.SetTop(selBorder, sel.Y);
        selBorder.Width = sel.Width; selBorder.Height = sel.Height;
        UpdateDim();
    }

    void UpdateSelectionVisuals()
    {
        selBorder.Stroke = Brushes.White; selBorder.StrokeThickness = 1.5; selBorder.RadiusX = selBorder.RadiusY = 0;
        ShowSelection();
        double W = ScreenBounds.Width, H = ScreenBounds.Height;
        SetGuide(guides[0], 0, sel.Top + 0.5, W, sel.Top + 0.5);
        SetGuide(guides[1], 0, sel.Bottom + 0.5, W, sel.Bottom + 0.5);
        SetGuide(guides[2], sel.Left + 0.5, 0, sel.Left + 0.5, H);
        SetGuide(guides[3], sel.Right + 0.5, 0, sel.Right + 0.5, H);
        sizeText.Text = $"{(int)sel.Width} × {(int)sel.Height}";
        sizePill.Visibility = Visibility.Visible;
        var d = Dims(sizePill);
        double px = sel.Right - d.Width, py = sel.Bottom + 10 * scale;
        if (py + d.Height > H) py = sel.Bottom - d.Height - 10 * scale;
        Put(sizePill, Math.Max(4, px), Math.Max(4, py));
    }

    static void SetGuide(Line l, double x1, double y1, double x2, double y2)
    { l.X1 = x1; l.Y1 = y1; l.X2 = x2; l.Y2 = y2; l.Visibility = Visibility.Visible; }

    /// <summary>11×11 pixel loupe next to the cursor with coordinates and the colour under it.</summary>
    void UpdateLoupe(Point p, bool show)
    {
        if (!show) { loupe.Visibility = Visibility.Collapsed; return; }
        int cx = Math.Clamp((int)p.X, 0, img.PixelWidth - 1), cy = Math.Clamp((int)p.Y, 0, img.PixelHeight - 1);
        int x0 = Math.Clamp(cx - 5, 0, Math.Max(0, img.PixelWidth - 11)), y0 = Math.Clamp(cy - 5, 0, Math.Max(0, img.PixelHeight - 11));
        try
        {
            var crop = new CroppedBitmap(img, new Int32Rect(x0, y0, Math.Min(11, img.PixelWidth), Math.Min(11, img.PixelHeight)));
            crop.Freeze();
            loupeImg.Source = crop;
            RenderOptions.SetBitmapScalingMode(loupeImg, BitmapScalingMode.NearestNeighbor);
            var px = new byte[4];
            new CroppedBitmap(new FormatConvertedBitmap(img, PixelFormats.Bgra32, null, 0), new Int32Rect(cx, cy, 1, 1)).CopyPixels(px, 4, 0);
            loupeSwatch.Background = new SolidColorBrush(Color.FromRgb(px[2], px[1], px[0]));
            loupeText.Text = $"x {ScreenBounds.X + cx} · y {ScreenBounds.Y + cy}";
        }
        catch { loupe.Visibility = Visibility.Collapsed; return; }
        loupe.Visibility = Visibility.Visible;
        var d = Dims(loupe);
        double lx = p.X + 28 * scale, ly = p.Y + 28 * scale;
        if (lx + d.Width > ScreenBounds.Width - 4) lx = p.X - d.Width - 28 * scale;
        if (ly + d.Height > ScreenBounds.Height - 4) ly = p.Y - d.Height - 28 * scale;
        Put(loupe, Math.Max(4, lx), Math.Max(4, ly));
    }

    void OnDown(object s, MouseButtonEventArgs e)
    {
        if (pickWindow)
        {
            if (hasPick && sel.Width >= 2 && sel.Height >= 2) Emit();
            return;
        }
        start = e.GetPosition(root);
        lastMouse = start;
        sel = new Rect(start, new Size(0, 0));
        dragging = true;
        crossH.Visibility = crossV.Visibility = Visibility.Collapsed;
        CaptureMouse();
    }

    void OnUp(object s, MouseButtonEventArgs e)
    {
        if (!dragging) return;
        dragging = false;
        ReleaseMouseCapture();
        int w = (int)sel.Width, h = (int)sel.Height;
        if (w < 3 || h < 3)
        {
            // treat as a stray click: reset and keep picking
            ResetVisuals();
            crossH.Visibility = crossV.Visibility = Visibility.Visible;
            return;
        }
        Emit();
    }

    void Emit()
    {
        int x = (int)sel.X, y = (int)sel.Y, w = (int)sel.Width, h = (int)sel.Height;
        w = Math.Min(w, img.PixelWidth - x);
        h = Math.Min(h, img.PixelHeight - y);
        var crop = new WriteableBitmap(new CroppedBitmap(img, new Int32Rect(x, y, w, h)));
        crop.Freeze();
        Picked?.Invoke(new Drawing.Rectangle(ScreenBounds.X + x, ScreenBounds.Y + y, w, h), crop);
    }
}
