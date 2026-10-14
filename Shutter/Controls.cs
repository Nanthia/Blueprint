using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;

namespace Shutter;

/// <summary>Draws one of the Icon.* geometries (24-unit grid) in the inherited Foreground colour.</summary>
public sealed class IconPresenter : Control
{
    public static readonly DependencyProperty IconProperty = DependencyProperty.Register(nameof(Icon), typeof(string), typeof(IconPresenter),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty SizeProperty = DependencyProperty.Register(nameof(Size), typeof(double), typeof(IconPresenter),
        new FrameworkPropertyMetadata(18.0, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty StrokeWidthProperty = DependencyProperty.Register(nameof(StrokeWidth), typeof(double), typeof(IconPresenter),
        new FrameworkPropertyMetadata(1.6, FrameworkPropertyMetadataOptions.AffectsRender));

    static IconPresenter()
    {
        ForegroundProperty.OverrideMetadata(typeof(IconPresenter),
            new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.Inherits));
        IsHitTestVisibleProperty.OverrideMetadata(typeof(IconPresenter), new UIPropertyMetadata(false));
        FocusableProperty.OverrideMetadata(typeof(IconPresenter), new FrameworkPropertyMetadata(false));
    }

    public string Icon { get => (string)GetValue(IconProperty); set => SetValue(IconProperty, value); }
    public double Size { get => (double)GetValue(SizeProperty); set => SetValue(SizeProperty, value); }
    public double StrokeWidth { get => (double)GetValue(StrokeWidthProperty); set => SetValue(StrokeWidthProperty, value); }

    protected override Size MeasureOverride(Size constraint) => new(Size, Size);

    static readonly Dictionary<string, Geometry?> cache = new();

    static Geometry? Get(string key)
    {
        if (cache.TryGetValue(key, out var g)) return g;
        g = Application.Current?.TryFindResource(key) as Geometry;
        if (g != null && g.CanFreeze) g.Freeze();
        cache[key] = g;
        return g;
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (string.IsNullOrEmpty(Icon)) return;
        var brush = Foreground;
        string k = "Icon." + Icon;
        double s = Size / 24.0;
        dc.PushTransform(new ScaleTransform(s, s));

        var pen = new Pen(brush, StrokeWidth) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        if (Get(k + ".Fill") is { } fill) dc.DrawGeometry(brush, null, fill);
        if (Get(k + ".FillDim") is { } fillDim) { dc.PushOpacity(0.5); dc.DrawGeometry(brush, null, fillDim); dc.Pop(); }
        if (Get(k) is { } stroke) dc.DrawGeometry(null, pen, stroke);
        if (Get(k + ".Dim") is { } dim) { dc.PushOpacity(0.45); dc.DrawGeometry(null, pen, dim); dc.Pop(); }
        if (Get(k + ".Dash") is { } dash)
        {
            var dp = new Pen(brush, StrokeWidth) { DashStyle = new DashStyle(new double[] { 1.25, 1.5 }, 0), DashCap = PenLineCap.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
            dc.DrawGeometry(null, dp, dash);
        }
        dc.Pop();
    }
}

/// <summary>The six-blade aperture mark from the brand kit (optionally on the rounded tile).</summary>
public sealed class ApertureLogo : FrameworkElement
{
    public static readonly DependencyProperty WithTileProperty = DependencyProperty.Register(nameof(WithTile), typeof(bool), typeof(ApertureLogo),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));
    public bool WithTile { get => (bool)GetValue(WithTileProperty); set => SetValue(WithTileProperty, value); }

    static readonly (string d, string fill)[] Blades =
    {
        ("M128.00 102.00 L150.52 115.00 L203.93 145.84 A78 78 0 0 0 181.42 71.16 Z", "#0A84FF"),
        ("M150.52 115.00 L150.52 141.00 L150.52 202.68 A78 78 0 0 0 203.93 145.84 Z", "#3B9DFF"),
        ("M150.52 141.00 L128.00 154.00 L74.58 184.84 A78 78 0 0 0 150.52 202.68 Z", "#0A6FDB"),
        ("M128.00 154.00 L105.48 141.00 L52.07 110.16 A78 78 0 0 0 74.58 184.84 Z", "#5AADFF"),
        ("M105.48 141.00 L105.48 115.00 L105.48 53.32 A78 78 0 0 0 52.07 110.16 Z", "#0A84FF"),
        ("M105.48 115.00 L128.00 102.00 L181.42 71.16 A78 78 0 0 0 105.48 53.32 Z", "#1F78E6"),
    };

    static readonly List<(Geometry g, Brush b)> parts = Blades.Select(b =>
    {
        var g = Geometry.Parse(b.d); g.Freeze();
        var br = (Brush)new BrushConverter().ConvertFromString(b.fill)!; br.Freeze();
        return (g, br);
    }).ToList();

    public static void Draw(DrawingContext dc, double size, bool tile)
    {
        double s = size / 256.0;
        dc.PushTransform(new ScaleTransform(s, s));
        if (tile)
        {
            var grad = new LinearGradientBrush(Color.FromRgb(0x34, 0x34, 0x3A), Color.FromRgb(0x17, 0x17, 0x1A), 90);
            dc.DrawRoundedRectangle(grad, null, new Rect(8, 8, 240, 240), 56, 56);
            var rim = new LinearGradientBrush(Color.FromArgb(46, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), 90);
            dc.DrawRoundedRectangle(null, new Pen(rim, 1.5), new Rect(8.75, 8.75, 238.5, 238.5), 55.25, 55.25);
        }
        var pen = new Pen(new SolidColorBrush(Color.FromRgb(0x1F, 0x1F, 0x23)), 5) { LineJoin = PenLineJoin.Round };
        foreach (var (g, b) in parts) dc.DrawGeometry(b, pen, g);
        dc.DrawEllipse(null, new Pen(new SolidColorBrush(Color.FromRgb(0x0E, 0x0E, 0x10)), 5), new Point(128, 128), 80.5, 80.5);
        dc.DrawEllipse(null, new Pen(new SolidColorBrush(Color.FromArgb(26, 255, 255, 255)), 2), new Point(128, 128), 84, 84);
        dc.Pop();
    }

    protected override void OnRender(DrawingContext dc) => Draw(dc, Math.Min(ActualWidth, ActualHeight), WithTile);
}

/// <summary>Borderless window with the Shutter title bar: logo, title, optional left/right slots, caption buttons.</summary>
public class ShutterWindow : Window
{
    static ShutterWindow()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(ShutterWindow), new FrameworkPropertyMetadata(typeof(ShutterWindow)));
    }

    public static readonly DependencyProperty SubtitleProperty = DependencyProperty.Register(nameof(Subtitle), typeof(string), typeof(ShutterWindow), new PropertyMetadata(""));
    public static readonly DependencyProperty TitleLeftProperty = DependencyProperty.Register(nameof(TitleLeft), typeof(object), typeof(ShutterWindow));
    public static readonly DependencyProperty TitleRightProperty = DependencyProperty.Register(nameof(TitleRight), typeof(object), typeof(ShutterWindow));
    public static readonly DependencyProperty ShowMaximizeProperty = DependencyProperty.Register(nameof(ShowMaximize), typeof(bool), typeof(ShutterWindow), new PropertyMetadata(true));
    public static readonly DependencyProperty ShowMinimizeProperty = DependencyProperty.Register(nameof(ShowMinimize), typeof(bool), typeof(ShutterWindow), new PropertyMetadata(true));

    public string Subtitle { get => (string)GetValue(SubtitleProperty); set => SetValue(SubtitleProperty, value); }
    public object? TitleLeft { get => GetValue(TitleLeftProperty); set => SetValue(TitleLeftProperty, value); }
    public object? TitleRight { get => GetValue(TitleRightProperty); set => SetValue(TitleRightProperty, value); }
    public bool ShowMaximize { get => (bool)GetValue(ShowMaximizeProperty); set => SetValue(ShowMaximizeProperty, value); }
    public bool ShowMinimize { get => (bool)GetValue(ShowMinimizeProperty); set => SetValue(ShowMinimizeProperty, value); }

    public ShutterWindow()
    {
        // implicit styles are not inherited by derived window types, so reference the base style explicitly
        SetResourceReference(StyleProperty, typeof(ShutterWindow));
        Icon = AppIcons.WindowIcon;
        WindowStyle = WindowStyle.None;
        WindowChrome.SetWindowChrome(this, new WindowChrome
        {
            CaptionHeight = 40,
            ResizeBorderThickness = new Thickness(6),
            GlassFrameThickness = new Thickness(0),
            CornerRadius = new CornerRadius(0),
            UseAeroCaptionButtons = false,
        });
        SourceInitialized += (_, _) => ApplyDwm();
        StateChanged += (_, _) => Padding = WindowState == WindowState.Maximized ? new Thickness(8) : new Thickness(0);
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        if (GetTemplateChild("PART_Min") is Button min) min.Click += (_, _) => WindowState = WindowState.Minimized;
        if (GetTemplateChild("PART_Max") is Button max) max.Click += (_, _) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        if (GetTemplateChild("PART_Close") is Button close) close.Click += (_, _) => Close();
    }

    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    void ApplyDwm()
    {
        var h = new WindowInteropHelper(this).Handle;
        int dark = 1, round = 2, border = 0x002F2A2A; // COLORREF 0x00BBGGRR for #2A2A2F
        DwmSetWindowAttribute(h, 20, ref dark, 4);   // immersive dark mode
        DwmSetWindowAttribute(h, 33, ref round, 4);  // rounded corners (Windows 11)
        DwmSetWindowAttribute(h, 34, ref border, 4); // border colour: no system accent
    }
}

/// <summary>Small helpers for building UI in code.</summary>
public static class Ui
{
    public static Brush B(string key) => (Brush)Application.Current.FindResource(key);
    public static CornerRadius Radius(string key) => (CornerRadius)Application.Current.FindResource(key);

    public static IconPresenter Icon(string name, double size = 18, Brush? fg = null)
    {
        var i = new IconPresenter { Icon = name, Size = size };
        if (fg != null) i.Foreground = fg;
        return i;
    }

    public static Border Keycap(string text)
    {
        return new Border
        {
            Background = B("Bg4Brush"), BorderBrush = B("Line2Brush"), BorderThickness = new Thickness(1),
            CornerRadius = Radius("RadiusXs"), Padding = new Thickness(6, 1, 6, 1), Margin = new Thickness(2, 0, 2, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = text, FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = B("Text2Brush") },
        };
    }

    /// <summary>"Ctrl+Shift+4" -> row of keycaps.</summary>
    public static StackPanel Keys(string combo)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var parts = combo.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (int i = 0; i < parts.Length; i++)
        {
            if (i > 0) sp.Children.Add(new TextBlock { Text = "+", Foreground = B("Text3Brush"), Margin = new Thickness(2, 0, 2, 0), VerticalAlignment = VerticalAlignment.Center, FontSize = 11 });
            sp.Children.Add(Keycap(parts[i]));
        }
        return sp;
    }

    /// <summary>Icon + text content for buttons.</summary>
    public static StackPanel IconLabel(string icon, string text, double iconSize = 16, double gap = 8)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        sp.Children.Add(Icon(icon, iconSize));
        sp.Children.Add(new TextBlock { Text = text, Margin = new Thickness(gap, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        return sp;
    }
}
