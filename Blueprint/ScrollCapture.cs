using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace Blueprint;

/// <summary>One captured frame, with a subsampled luma plane used to find how far the content scrolled.</summary>
sealed class StitchFrame
{
    public byte[] Bgra = null!;
    public byte[] Luma = null!;
    public int W, H, SW;

    public static StitchFrame From(byte[] bgra, int w, int h, bool flip)
    {
        int stride = w * 4;
        if (flip)
        {
            var f = new byte[bgra.Length];
            for (int y = 0; y < h; y++) Buffer.BlockCopy(bgra, y * stride, f, (h - 1 - y) * stride, stride);
            bgra = f;
        }
        int sw = Math.Max(1, w / 2);
        var luma = new byte[h * sw];
        for (int y = 0; y < h; y++)
        {
            int row = y * stride, o = y * sw;
            for (int x = 0; x < sw; x++)
            {
                int i = row + x * 8; // every second pixel
                luma[o + x] = (byte)((bgra[i + 2] * 77 + bgra[i + 1] * 150 + bgra[i] * 29) >> 8);
            }
        }
        return new StitchFrame { Bgra = bgra, Luma = luma, W = w, H = h, SW = sw };
    }
}

/// <summary>
/// Stitches successive frames of a scrolling region. Frames are always processed as "content moves up"
/// (scrolling down); upward scrolling is handled by feeding vertically flipped frames.
/// </summary>
sealed class Stitcher
{
    public enum Result { NoMove, Added, NoMatch }

    readonly int w, h;
    readonly List<byte[]> strips = new();
    int bodyRows;
    byte[]? footer;
    int footerRows;
    StitchFrame last;

    public int TotalRows => bodyRows + footerRows;
    public int LastShift { get; private set; }

    public Stitcher(StitchFrame first)
    {
        w = first.W; h = first.H; last = first;
        AddRows(first.Bgra, 0, h);
    }

    void AddRows(byte[] bgra, int row, int count)
    {
        var s = new byte[count * w * 4];
        Buffer.BlockCopy(bgra, row * w * 4, s, 0, s.Length);
        strips.Add(s);
        bodyRows += count;
    }

    /// <summary>Two rows "match" if no more than a tiny fraction of sampled pixels differ noticeably.</summary>
    static bool RowMatch(byte[] a, int ra, byte[] b, int rb, int sw)
    {
        int oa = ra * sw, ob = rb * sw, allowed = sw / 300, bad = 0;
        for (int i = 0; i < sw; i++)
        {
            int d = a[oa + i] - b[ob + i];
            if (d > 32 || d < -32) { if (++bad > allowed) return false; }
        }
        return true;
    }

    int FixedBottom(StitchFrame a, StitchFrame b)
    {
        int cap = (int)(h * 0.35), k = 0;
        while (k < cap && RowMatch(a.Luma, h - 1 - k, b.Luma, h - 1 - k, a.SW)) k++;
        if (k < 8) return 0;
        // identical blank rows are just whitespace, not a sticky footer: require some texture
        int distinct = 0;
        for (int i = 0; i < k - 1; i++) if (!RowMatch(a.Luma, h - 1 - i, a.Luma, h - 2 - i, a.SW)) distinct++;
        return distinct >= k * 0.15 ? k : 0;
    }

    (int dy, double frac) Scan(StitchFrame a, StitchFrame b, int eff, int lo, int hi, int expected)
    {
        var cands = new List<(int dy, int m)>();
        int bestM = -1;
        for (int dy = Math.Max(1, lo); dy <= hi; dy++)
        {
            int overlap = eff - dy, maxBad = (int)(overlap * 0.2), bad = 0, m = 0;
            for (int r = 0; r < overlap; r++)
            {
                if (RowMatch(a.Luma, dy + r, b.Luma, r, a.SW)) m++;
                else if (++bad > maxBad) { m = -1; break; }
            }
            if (m >= 0) { cands.Add((dy, m)); if (m > bestM) bestM = m; }
        }
        if (cands.Count == 0) return (0, 0);
        var pick = cands.Where(c => c.m >= bestM * 0.97).OrderBy(c => Math.Abs(c.dy - expected)).First();
        return (pick.dy, (double)pick.m / (eff - pick.dy));
    }

    int FindShift(StitchFrame a, StitchFrame b, int eff, int expected)
    {
        int minOverlap = Math.Max(24, (int)(h * 0.12));
        int maxDy = eff - minOverlap;
        if (maxDy < 1) return 0;
        if (expected > 0)
        {
            var (dy, frac) = Scan(a, b, eff, (int)(expected * 0.6), Math.Min(maxDy, (int)(expected * 1.4) + 2), expected);
            if (dy > 0 && frac >= 0.85) return dy;
        }
        var full = Scan(a, b, eff, 1, maxDy, expected > 0 ? expected : h / 2);
        return full.frac >= 0.8 ? full.dy : 0;
    }

    public Result Add(StitchFrame b, int expected)
    {
        var a = last;
        int allMatch = 0;
        for (int r = 0; r < h; r += 2) if (RowMatch(a.Luma, r, b.Luma, r, a.SW)) allMatch++;
        if (allMatch >= (h + 1) / 2 * 0.995) return Result.NoMove;

        int f = FixedBottom(a, b);
        int eff = h - f;
        int dy = FindShift(a, b, eff, expected);
        if (dy <= 0) return Result.NoMatch;

        if (f > 0 && footerRows == 0 && strips.Count == 1 && bodyRows == h)
        {
            // the first frame included the sticky footer: it belongs only at the very end
            var s = strips[0];
            var trimmed = new byte[(h - f) * w * 4];
            Buffer.BlockCopy(s, 0, trimmed, 0, trimmed.Length);
            strips[0] = trimmed;
            bodyRows -= f;
        }
        AddRows(b.Bgra, eff - dy, dy);
        if (f > 0)
        {
            footer = new byte[f * w * 4];
            Buffer.BlockCopy(b.Bgra, (h - f) * w * 4, footer, 0, footer.Length);
            footerRows = f;
        }
        LastShift = dy;
        last = b;
        return Result.Added;
    }

    public BitmapSource Build(bool flip)
    {
        int total = TotalRows, stride = w * 4;
        var buf = new byte[total * stride];
        int y = 0;
        foreach (var s in strips) { Buffer.BlockCopy(s, 0, buf, y * stride, s.Length); y += s.Length / stride; }
        if (footer != null) Buffer.BlockCopy(footer, 0, buf, y * stride, footer.Length);
        if (flip)
        {
            var f = new byte[buf.Length];
            for (int r = 0; r < total; r++) Buffer.BlockCopy(buf, r * stride, f, (total - 1 - r) * stride, stride);
            buf = f;
        }
        var bs = BitmapSource.Create(w, total, 96, 96, PixelFormats.Bgr32, null, buf, stride);
        bs.Freeze();
        return bs;
    }
}

/// <summary>Region -> choose a mode -> frames are captured, shifts detected, rows stitched -> final tall image.</summary>
sealed class ScrollSession
{
    readonly Drawing.Rectangle region;
    readonly Action<BitmapSource?> done;
    ScrollHud hud = null!;
    ScrollBorder border = null!;
    bool finish, cancel, started;

    ScrollSession(Drawing.Rectangle region, Action<BitmapSource?> done) { this.region = region; this.done = done; }

    public static void Begin(Drawing.Rectangle region, Action<BitmapSource?> done)
    {
        var s = new ScrollSession(region, done);
        s.border = new ScrollBorder(region);
        s.border.Show();
        s.hud = new ScrollHud(region);
        s.hud.Chosen += mode => { if (!s.started) { s.started = true; s.Run(mode); } };
        s.hud.DoneClicked += () => s.finish = true;
        s.hud.CancelClicked += () => { s.cancel = true; s.finish = true; if (!s.started) s.Close(null); };
        s.hud.Show();
    }

    void Close(BitmapSource? result)
    {
        try { hud.Close(); border.Close(); } catch { }
        done(result);
    }

    static bool NearlyEqual(byte[] a, byte[] b)
    {
        if (a.Length != b.Length) return false;
        int bad = 0, n = 0;
        for (int i = 0; i < a.Length; i += 64, n++)
            if (Math.Abs(a[i] - b[i]) > 24 || Math.Abs(a[i + 1] - b[i + 1]) > 24) bad++;
        return bad <= n * 0.002;
    }

    async Task<(byte[] bgra, int w, int h)> StableGrab()
    {
        var prev = ScreenCapture.GrabBytes(region);
        for (int i = 0; i < 8; i++)
        {
            await Task.Delay(70);
            var cur = ScreenCapture.GrabBytes(region);
            if (NearlyEqual(prev.bgra, cur.bgra)) return cur;
            prev = cur;
        }
        return prev;
    }

    async void Run(ScrollHud.Mode mode)
    {
        BitmapSource? result = null;
        try
        {
            int W = region.Width, H = region.Height;
            if (H < 160 || W < 40)
            {
                hud.SetStatus("Select a taller area and try again");
                await Task.Delay(1800);
                Close(null);
                return;
            }
            hud.Running();
            await Task.Delay(300); // let the selection overlay disappear

            bool auto = mode != ScrollHud.Mode.Manual;
            bool flip = mode == ScrollHud.Mode.AutoUp;
            int cx = region.X + W / 2, cy = region.Y + H / 2;
            int max = Math.Max(1000, Settings.Current.ScrollMaxHeight);

            var first = ScreenCapture.GrabBytes(region);
            var firstBytes = first.bgra;
            var st = new Stitcher(StitchFrame.From(first.bgra, W, H, flip));

            int notches = 2, noMove = 0, fails = 0;
            double pxPerNotch = 0;
            bool directionKnown = auto;
            string endNote = "";

            while (!finish)
            {
                if ((Native.GetAsyncKeyState(0x1B) & 0x8000) != 0) { finish = true; break; }
                if (st.TotalRows >= max) { endNote = "reached the maximum height"; break; }

                if (auto)
                {
                    Native.SetCursorPos(cx, cy);
                    int delta = 120 * notches * (flip ? 1 : -1);
                    Native.mouse_event(0x0800 /* MOUSEEVENTF_WHEEL */, 0, 0, unchecked((uint)delta), UIntPtr.Zero);
                    await Task.Delay(120);
                }
                else await Task.Delay(100);

                var g = await StableGrab();
                var frame = StitchFrame.From(g.bgra, W, H, flip);
                int expected = auto && pxPerNotch > 0 ? (int)(pxPerNotch * notches) : st.LastShift;
                var res = await Task.Run(() => st.Add(frame, expected));

                if (res == Stitcher.Result.NoMatch && !auto && !directionKnown)
                {
                    // manual mode: maybe the user scrolled the other way
                    var alt = new Stitcher(StitchFrame.From(firstBytes, W, H, !flip));
                    var altFrame = StitchFrame.From(g.bgra, W, H, !flip);
                    if (await Task.Run(() => alt.Add(altFrame, 0)) == Stitcher.Result.Added) { st = alt; flip = !flip; res = Stitcher.Result.Added; }
                }

                switch (res)
                {
                    case Stitcher.Result.Added:
                        noMove = 0; fails = 0; directionKnown = true;
                        if (auto && st.LastShift > 0)
                        {
                            pxPerNotch = (double)st.LastShift / notches;
                            notches = Math.Clamp((int)Math.Round(H * 0.55 / Math.Max(1, pxPerNotch)), 1, 12);
                        }
                        hud.SetCaptured(st.TotalRows, max);
                        break;
                    case Stitcher.Result.NoMove:
                        noMove++;
                        if (auto && noMove >= 2) { endNote = "reached the end"; finish = true; }
                        break;
                    default:
                        fails++;
                        hud.SetStatus(auto ? "can't match frames…" : "scroll slower — frames didn't overlap");
                        if (auto && fails >= 3) { endNote = "couldn't match the frames"; finish = true; }
                        break;
                }
            }

            if (!cancel && st.TotalRows > H)
            {
                hud.SetStatus("stitching…");
                result = await Task.Run(() => st.Build(flip));
            }
            else if (!cancel) { result = await Task.Run(() => st.Build(flip)); }
            if (!string.IsNullOrEmpty(endNote) && result != null) hud.SetStatus(endNote);
        }
        catch (Exception ex)
        {
            try { System.IO.File.AppendAllText(System.IO.Path.Combine(Settings.Dir, "error.log"), $"{DateTime.Now}: scroll capture: {ex}\n\n"); } catch { }
        }
        finally
        {
            Close(cancel ? null : result);
        }
    }
}

/// <summary>Control strip for scrolling capture. Excluded from screen capture so it never appears in the result.</summary>
sealed class ScrollHud : Window
{
    public enum Mode { AutoDown, AutoUp, Manual }
    public event Action<Mode>? Chosen;
    public event Action? DoneClicked, CancelClicked;

    readonly Drawing.Rectangle region;
    readonly StackPanel chooseRow = new() { Orientation = Orientation.Horizontal };
    readonly StackPanel runRow = new() { Orientation = Orientation.Horizontal, Visibility = Visibility.Collapsed };
    readonly TextBlock capturedText = new() { FontSize = 15, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock capturedSub = new() { Text = "captured", Foreground = Ui.B("Text2Brush"), FontSize = 15, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
    readonly Border barFill = new() { Height = 4, CornerRadius = new CornerRadius(2), Background = Ui.B("AccentBrush"), HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
    readonly StackPanel hintRow = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 12, 0, 0) };
    Mode selected = Mode.AutoDown;

    public ScrollHud(Drawing.Rectangle region)
    {
        this.region = region;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        ShowActivated = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        Title = "Blueprint scrolling capture";
        FontFamily = (FontFamily)Application.Current.FindResource("UiFont");
        FontSize = 14;
        Foreground = Ui.B("TextBrush");

        // choose state: Scroll [Auto down | Auto up | Manual]  Start  ✕
        var logo = new ApertureLogo { Width = 24, Height = 24, Margin = new Thickness(6, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
        chooseRow.Children.Add(logo);
        chooseRow.Children.Add(new TextBlock { Text = "Scroll", Foreground = Ui.B("Text2Brush"), VerticalAlignment = VerticalAlignment.Center, FontSize = 15, Margin = new Thickness(0, 0, 14, 0) });
        var seg = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var (m, icon, label) in new[] { (Mode.AutoDown, "Arrowdown", "Auto down"), (Mode.AutoUp, "Arrowup", "Auto up"), (Mode.Manual, "Hand", "Manual") })
        {
            var rb = new RadioButton { Style = (Style)FindResource("SegmentItem"), GroupName = "scrollmode", IsChecked = m == Mode.AutoDown, Padding = new Thickness(14, 8, 16, 8), FontSize = 15 };
            rb.Content = Ui.IconLabel(icon, label, 16, 8);
            var mm = m;
            rb.Checked += (_, _) => selected = mm;
            seg.Children.Add(rb);
        }
        chooseRow.Children.Add(new Border { Style = (Style)FindResource("SegmentBox"), Child = seg });
        chooseRow.Children.Add(new Border { Width = 1, Height = 26, Background = Ui.B("Line2Brush"), Margin = new Thickness(14, 0, 14, 0) });
        var start = new Button { Style = (Style)FindResource("PrimaryButton"), Content = "Start", Height = 40, MinWidth = 84, FontSize = 15, ToolTip = "Start scrolling capture" };
        start.Click += (_, _) => Chosen?.Invoke(selected);
        chooseRow.Children.Add(start);
        chooseRow.Children.Add(CloseButton());

        // running state: ● 2,400 px captured ▬▬  ✔ Done  ✕
        var dot = new Border { Width = 24, Height = 24, CornerRadius = new CornerRadius(12), Background = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0x5A, 0x50)), Margin = new Thickness(6, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center,
            Child = new Border { Width = 10, Height = 10, CornerRadius = new CornerRadius(5), Background = Ui.B("DangerBrush"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
        var rlogo = new ApertureLogo { Width = 24, Height = 24, Margin = new Thickness(6, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
        runRow.Children.Add(rlogo);
        runRow.Children.Add(dot);
        runRow.Children.Add(capturedText);
        runRow.Children.Add(capturedSub);
        var track = new Grid { Width = 150, Height = 4, Margin = new Thickness(16, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        track.Children.Add(new Border { Background = Ui.B("Bg4Brush"), CornerRadius = new CornerRadius(2) });
        track.Children.Add(barFill);
        track.SizeChanged += (_, e) => trackWidth = e.NewSize.Width;
        runRow.Children.Add(track);
        runRow.Children.Add(new Border { Width = 1, Height = 26, Background = Ui.B("Line2Brush"), Margin = new Thickness(16, 0, 14, 0) });
        var done = new Button { Style = (Style)FindResource("PrimaryButton"), Content = Ui.IconLabel("Check", "Done", 17, 8), Height = 40, MinWidth = 98, FontSize = 15, ToolTip = "Finish and stitch (Esc)" };
        done.Click += (_, _) => DoneClicked?.Invoke();
        runRow.Children.Add(done);
        runRow.Children.Add(CloseButton());

        var card = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x27)), BorderBrush = Ui.B("Line2Brush"), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(18), Padding = new Thickness(10, 8, 10, 8), HorizontalAlignment = HorizontalAlignment.Center,
        };
        var cardHost = new Grid();
        cardHost.Children.Add(chooseRow);
        cardHost.Children.Add(runRow);
        card.Child = cardHost;

        void Hint(string text) { hintRow.Children.Clear(); hintRow.Children.Add(Ui.Keycap("Esc")); hintRow.Children.Add(new TextBlock { Text = text, Foreground = new SolidColorBrush(Color.FromRgb(0xED, 0xED, 0xF0)), Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center }); }
        Hint("cancels");
        hintUpdater = Hint;
        var hintPill = new Border { Background = new SolidColorBrush(Color.FromArgb(200, 28, 28, 32)), CornerRadius = new CornerRadius(14), Padding = new Thickness(12, 5, 14, 5), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 12, 0, 0), Child = hintRow };
        hintRow.Margin = new Thickness(0);

        var stack = new StackPanel { Margin = new Thickness(16, 8, 16, 18) };
        stack.Children.Add(card);
        stack.Children.Add(hintPill);
        stack.Effect = (System.Windows.Media.Effects.Effect)FindResource("PopoverShadow");
        Content = stack;

        SourceInitialized += (_, _) =>
        {
            var h = new WindowInteropHelper(this).Handle;
            int ex = Native.GetWindowLong(h, Native.GWL_EXSTYLE);
            Native.SetWindowLong(h, Native.GWL_EXSTYLE, ex | Native.WS_EX_NOACTIVATE | Native.WS_EX_TOOLWINDOW);
            if (Environment.GetEnvironmentVariable("BLUEPRINT_SHOW_HUD") != "1")
                Native.SetWindowDisplayAffinity(h, 0x11 /* WDA_EXCLUDEFROMCAPTURE */);
        };
        ContentRendered += (_, _) => Place();
        KeyDown += (_, e) => { if (e.Key == Key.Escape) CancelClicked?.Invoke(); };
    }

    double trackWidth = 150;
    readonly Action<string> hintUpdater;

    Button CloseButton()
    {
        var b = new Button { Style = (Style)FindResource("IconButton"), Content = Ui.Icon("Close", 18), Width = 40, Height = 40, Margin = new Thickness(6, 0, 0, 0), ToolTip = "Cancel" };
        b.Click += (_, _) => CancelClicked?.Invoke();
        return b;
    }

    public void SetStatus(string s)
    {
        // transient messages (errors, hints) replace the caption text
        capturedSub.Text = s;
    }

    public void Running()
    {
        chooseRow.Visibility = Visibility.Collapsed;
        runRow.Visibility = Visibility.Visible;
        capturedText.Text = "0 px";
        capturedSub.Text = "captured";
        hintUpdater("or Done to finish");
    }

    /// <summary>Live progress: rows captured so far against the configured maximum.</summary>
    public void SetCaptured(int rows, int max)
    {
        capturedText.Text = $"{rows:N0} px";
        capturedSub.Text = "captured";
        barFill.Width = trackWidth * Math.Clamp((double)rows / Math.Max(1, max), 0, 1);
    }

    /// <summary>Put the strip just below the region (or above / inside when there is no room).</summary>
    void Place()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        double s = VisualTreeHelper.GetDpi(this).DpiScaleX;
        int pw = (int)Math.Ceiling(ActualWidth * s), ph = (int)Math.Ceiling(ActualHeight * s);
        var screen = Forms.Screen.FromRectangle(region).Bounds;
        int x = Math.Clamp(region.X + region.Width / 2 - pw / 2, screen.Left, Math.Max(screen.Left, screen.Right - pw));
        int y = region.Bottom + 6;
        if (y + ph > screen.Bottom) y = region.Y - ph - 6;
        if (y < screen.Top) y = Math.Max(screen.Top, region.Bottom - ph - 10);
        Native.SetWindowPos(hwnd, Native.HWND_TOPMOST, x, y, pw, ph, Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);
    }
}

/// <summary>Thin accent outline around the capture region (click-through, excluded from capture).</summary>
sealed class ScrollBorder : Window
{
    readonly Drawing.Rectangle phys;
    readonly Border outline = new() { BorderBrush = new SolidColorBrush(Color.FromRgb(0x0A, 0x84, 0xFF)), BorderThickness = new Thickness(2), Background = Brushes.Transparent, CornerRadius = new CornerRadius(3) };

    public ScrollBorder(Drawing.Rectangle region)
    {
        phys = Drawing.Rectangle.Inflate(region, 3, 3);
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        ShowActivated = false;
        Left = phys.X; Top = phys.Y; Width = 50; Height = 50;
        outline.Width = phys.Width; outline.Height = phys.Height;
        Content = outline;

        SourceInitialized += (_, _) =>
        {
            var h = new WindowInteropHelper(this).Handle;
            int ex = Native.GetWindowLong(h, Native.GWL_EXSTYLE);
            Native.SetWindowLong(h, Native.GWL_EXSTYLE, ex | Native.WS_EX_TRANSPARENT | Native.WS_EX_NOACTIVATE | Native.WS_EX_TOOLWINDOW);
            if (Environment.GetEnvironmentVariable("BLUEPRINT_SHOW_HUD") != "1")
                Native.SetWindowDisplayAffinity(h, 0x11);
        };
        ContentRendered += (_, _) => Fix();
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        Dispatcher.BeginInvoke(new Action(Fix));
    }

    void Fix()
    {
        var h = new WindowInteropHelper(this).Handle;
        Native.SetWindowPos(h, Native.HWND_TOPMOST, phys.X, phys.Y, phys.Width, phys.Height, Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);
        double s = VisualTreeHelper.GetDpi(this).DpiScaleX;
        outline.LayoutTransform = new ScaleTransform(1 / s, 1 / s);
    }
}
