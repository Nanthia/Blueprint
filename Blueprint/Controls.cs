using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;

namespace Blueprint;

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

/// <summary>The Blueprint bunny mark (from brand/bunny/bunny-app-icon.svg), optionally on the rounded app tile.</summary>
public sealed class ApertureLogo : FrameworkElement
{
    public static readonly DependencyProperty WithTileProperty = DependencyProperty.Register(nameof(WithTile), typeof(bool), typeof(ApertureLogo),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));
    public bool WithTile { get => (bool)GetValue(WithTileProperty); set => SetValue(WithTileProperty, value); }

    static Geometry G(string d) { var g = Geometry.Parse(d); g.Freeze(); return g; }
    static SolidColorBrush Sb(string hex, double opacity = 1)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)) { Opacity = opacity };
        b.Freeze();
        return b;
    }

    static readonly Geometry EarL = G("M86 116 C70 82 68 44 84 28 C100 14 118 34 120 70 C121 90 118 106 112 118 Z");
    static readonly Geometry EarLInner = G("M90 108 C80 82 80 52 88 42 C97 33 107 48 108 74 C109 90 107 100 104 110 Z");
    static readonly Geometry EarR = G("M146 118 C140 90 142 64 158 54 C172 45 192 52 202 70 C206 78 200 85 192 80 C182 74 174 72 170 80 C166 90 168 104 170 118 Z");
    static readonly Geometry EarRInner = G("M152 110 C148 90 150 70 160 62 C170 56 184 60 192 70 C182 66 172 66 166 74 C162 84 162 98 164 110 Z");
    static readonly Geometry Head = G("M128 92 C178 92 214 120 214 160 C214 196 178 214 128 214 C78 214 42 196 42 160 C42 120 78 92 128 92 Z");
    static readonly Geometry Nose = G("M117 152 C117 146 139 145 141 151 C142 156 134 162 129 162 C123 162 117 157 117 152 Z");
    static readonly Geometry Mouth = G("M129 161 L129 176 M129 176 L120 186 M129 176 L139 185");

    static readonly Brush Fur = Frozen(new LinearGradientBrush(Color.FromRgb(0xD7, 0xD8, 0xDC), Color.FromRgb(0xB9, 0xBC, 0xC2), new Point(0, 0), new Point(0, 1)));
    static readonly Brush Muzzle = Frozen(new RadialGradientBrush(Color.FromRgb(0xFB, 0xF3, 0xEA), Color.FromRgb(0xEA, 0xDC, 0xCB)) { Center = new Point(.5, .4), GradientOrigin = new Point(.5, .4), RadiusX = .6, RadiusY = .6 });
    static readonly Brush Inner = Sb("#F2C9B8"), Cheek = Sb("#F09A8A", .35), Brown = Sb("#5A3328"), EyeB = Sb("#141416"), White = Sb("#FFFFFF");
    static readonly Pen MouthPen = FrozenPen(new Pen(Brown, 4) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round });

    static Brush Frozen(Brush b) { b.Freeze(); return b; }
    static Pen FrozenPen(Pen p) { p.Freeze(); return p; }

    /// <summary>Draws the bunny. With <paramref name="tile"/> it sits on the rounded app tile (256-unit canvas); without it the head fills the box.</summary>
    public static void Draw(DrawingContext dc, double size, bool tile)
    {
        if (tile)
        {
            double s = size / 256.0;
            dc.PushTransform(new ScaleTransform(s, s));
            var grad = new LinearGradientBrush(Color.FromRgb(0x34, 0x34, 0x3A), Color.FromRgb(0x17, 0x17, 0x1A), new Point(0, 0), new Point(0, 1));
            dc.DrawRoundedRectangle(grad, null, new Rect(8, 8, 240, 240), 56, 56);
            dc.PushClip(new RectangleGeometry(new Rect(8, 8, 240, 240), 56, 56));
            DrawBunny(dc);
            dc.Pop();
            dc.DrawRoundedRectangle(null, new Pen(new SolidColorBrush(Color.FromArgb(31, 255, 255, 255)), 1.5), new Rect(8.75, 8.75, 238.5, 238.5), 55.25, 55.25);
            dc.Pop();
        }
        else
        {
            // bunny-mark.svg viewBox: 20 10 216 216
            double s = size / 216.0;
            dc.PushTransform(new ScaleTransform(s, s));
            dc.PushTransform(new TranslateTransform(-20, -10));
            DrawBunny(dc);
            dc.Pop();
            dc.Pop();
        }
    }

    static void DrawBunny(DrawingContext dc)
    {
        dc.DrawGeometry(Fur, null, EarL);
        dc.DrawGeometry(Inner, null, EarLInner);
        dc.DrawGeometry(Fur, null, EarR);
        dc.DrawGeometry(Inner, null, EarRInner);
        dc.DrawGeometry(Fur, null, Head);
        dc.DrawEllipse(Muzzle, null, new Point(128, 170), 38, 31);
        dc.DrawEllipse(Cheek, null, new Point(78, 170), 12, 7);
        dc.DrawEllipse(Cheek, null, new Point(178, 166), 12, 7);
        dc.DrawGeometry(Brown, null, Nose);
        dc.DrawGeometry(null, MouthPen, Mouth);
        dc.DrawEllipse(EyeB, null, new Point(85, 145), 9, 10);
        dc.DrawEllipse(White, null, new Point(88, 141.5), 2.6, 2.6);
        dc.DrawEllipse(EyeB, null, new Point(171, 140), 9, 10);
        dc.DrawEllipse(White, null, new Point(174, 136.5), 2.6, 2.6);
    }

    protected override void OnRender(DrawingContext dc) => Draw(dc, Math.Min(ActualWidth, ActualHeight), WithTile);
}

/// <summary>Borderless window with the Blueprint title bar: logo, title, optional left/right slots, caption buttons.</summary>
public class BlueprintWindow : Window
{
    static BlueprintWindow()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(BlueprintWindow), new FrameworkPropertyMetadata(typeof(BlueprintWindow)));
    }

    public static readonly DependencyProperty SubtitleProperty = DependencyProperty.Register(nameof(Subtitle), typeof(string), typeof(BlueprintWindow), new PropertyMetadata(""));
    public static readonly DependencyProperty TitleLeftProperty = DependencyProperty.Register(nameof(TitleLeft), typeof(object), typeof(BlueprintWindow));
    public static readonly DependencyProperty TitleRightProperty = DependencyProperty.Register(nameof(TitleRight), typeof(object), typeof(BlueprintWindow));
    public static readonly DependencyProperty ShowMaximizeProperty = DependencyProperty.Register(nameof(ShowMaximize), typeof(bool), typeof(BlueprintWindow), new PropertyMetadata(true));
    public static readonly DependencyProperty ShowMinimizeProperty = DependencyProperty.Register(nameof(ShowMinimize), typeof(bool), typeof(BlueprintWindow), new PropertyMetadata(true));

    public string Subtitle { get => (string)GetValue(SubtitleProperty); set => SetValue(SubtitleProperty, value); }
    public object? TitleLeft { get => GetValue(TitleLeftProperty); set => SetValue(TitleLeftProperty, value); }
    public object? TitleRight { get => GetValue(TitleRightProperty); set => SetValue(TitleRightProperty, value); }
    public bool ShowMaximize { get => (bool)GetValue(ShowMaximizeProperty); set => SetValue(ShowMaximizeProperty, value); }
    public bool ShowMinimize { get => (bool)GetValue(ShowMinimizeProperty); set => SetValue(ShowMinimizeProperty, value); }

    public BlueprintWindow()
    {
        // implicit styles are not inherited by derived window types, so reference the base style explicitly
        SetResourceReference(StyleProperty, typeof(BlueprintWindow));
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
