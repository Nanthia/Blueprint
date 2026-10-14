using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Blueprint;

/// <summary>Backdrop ("pretty background") options. Modes: solid | gradient | image | mix (colours blended from the screenshot).</summary>
public sealed class BackdropSettings
{
    public bool Enabled { get; set; }
    public string Mode { get; set; } = "gradient";
    public string Color1 { get; set; } = "#FF512F";
    public string Color2 { get; set; } = "#F09819";
    public string GradientType { get; set; } = "linear"; // linear | radial
    public int Angle { get; set; } = 135;
    public int Padding { get; set; } = 64;
    public int InnerRadius { get; set; } = 10;  // screenshot corners
    public int OuterRadius { get; set; } = 0;   // whole canvas corners
    public int Shadow { get; set; } = 40;
    public int Blur { get; set; } = 20;          // image mode
    public string Aspect { get; set; } = "auto"; // auto | 16:9 | 4:3 | 3:2 | 1:1 | 9:16 | 4:5
    public string ImagePath { get; set; } = "";
    public bool UseWallpaper { get; set; }

    public BackdropSettings Clone() => (BackdropSettings)MemberwiseClone();

    public string Key() => string.Join("|", Mode, Color1, Color2, GradientType, Angle, Padding, InnerRadius, OuterRadius, Shadow, Blur, Aspect, ImagePath, UseWallpaper);
}

static class BackdropRenderer
{
    public static readonly (string from, string to, string name)[] Presets =
    {
        ("#FF512F", "#F09819", "Sunset"), ("#2193B0", "#6DD5ED", "Ocean"), ("#7F00FF", "#E100FF", "Violet"),
        ("#11998E", "#38EF7D", "Mint"), ("#0F2027", "#2C5364", "Night"), ("#FFAFBD", "#FFC3A0", "Peach"),
        ("#485563", "#29323C", "Slate"), ("#F953C6", "#B91D73", "Candy"),
    };

    public static Color Parse(string hex)
    {
        try { return (Color)ColorConverter.ConvertFromString(hex); }
        catch { return Colors.Gray; }
    }

    public static string ToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    static bool TryAspect(string a, out double ratio)
    {
        ratio = 0;
        var parts = a.Split(':');
        if (parts.Length == 2 && double.TryParse(parts[0], out var x) && double.TryParse(parts[1], out var y) && x > 0 && y > 0) { ratio = x / y; return true; }
        return false;
    }

    /// <summary>Canvas size and where the screenshot sits inside it.</summary>
    public static (Size canvas, Point origin) Layout(BackdropSettings s, int iw, int ih)
    {
        double w = iw + 2.0 * s.Padding, h = ih + 2.0 * s.Padding;
        if (TryAspect(s.Aspect, out var r))
        {
            if (w / h < r) w = h * r; else h = w / r;
        }
        w = Math.Ceiling(w); h = Math.Ceiling(h);
        return (new Size(w, h), new Point(Math.Round((w - iw) / 2), Math.Round((h - ih) / 2)));
    }

    /// <summary>Background fill + drop shadow, rendered once and cached by the surface.</summary>
    public static BitmapSource Build(BackdropSettings s, Size canvas, Rect imageRect, BitmapSource baseImage)
    {
        int w = Math.Max(1, (int)canvas.Width), h = Math.Max(1, (int)canvas.Height);
        var dv = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(dv, BitmapScalingMode.HighQuality);
        using (var dc = dv.RenderOpen())
        {
            var full = new Rect(0, 0, w, h);
            DrawFill(dc, s, full, baseImage);
            DrawShadow(dc, s, imageRect);
        }
        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        rtb.Freeze();
        return rtb;
    }

    static void DrawFill(DrawingContext dc, BackdropSettings s, Rect full, BitmapSource baseImage)
    {
        var c1 = Parse(s.Color1); var c2 = Parse(s.Color2);
        switch (s.Mode)
        {
            case "solid":
                dc.DrawRectangle(new SolidColorBrush(c1), null, full);
                break;
            case "image":
                var bg = LoadBackdropImage(s);
                if (bg != null) { DrawCover(dc, bg, full, s.Blur); break; }
                goto default;
            case "mix":
                // blend the screenshot's own colours: shrink to a few pixels and stretch back smoothly
                double sx = 6.0 / Math.Max(1, baseImage.PixelWidth), sy = 6.0 / Math.Max(1, baseImage.PixelHeight);
                var tiny = new TransformedBitmap(baseImage, new ScaleTransform(sx, sy));
                dc.DrawImage(tiny, full);
                dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(40, 0, 0, 0)), null, full);
                break;
            default:
                Brush b;
                if (s.GradientType == "radial")
                    b = new RadialGradientBrush(c1, c2) { Center = new Point(0.5, 0.5), GradientOrigin = new Point(0.5, 0.5), RadiusX = 0.75, RadiusY = 0.75 };
                else
                {
                    double a = s.Angle * Math.PI / 180;
                    b = new LinearGradientBrush(c1, c2, new Point(0.5 - Math.Cos(a) / 2, 0.5 - Math.Sin(a) / 2), new Point(0.5 + Math.Cos(a) / 2, 0.5 + Math.Sin(a) / 2));
                }
                dc.DrawRectangle(b, null, full);
                break;
        }
    }

    static void DrawCover(DrawingContext dc, BitmapSource img, Rect full, int blur)
    {
        BitmapSource src = img;
        if (blur > 0)
        {
            // cheap blur: shrink, then let the smooth upscale do the rest
            double k = 1 + blur / 3.0;
            var t = new TransformedBitmap(img, new ScaleTransform(1 / k, 1 / k));
            t.Freeze();
            src = t;
        }
        double scale = Math.Max(full.Width / src.PixelWidth, full.Height / src.PixelHeight);
        double dw = src.PixelWidth * scale, dh = src.PixelHeight * scale;
        dc.DrawImage(src, new Rect((full.Width - dw) / 2, (full.Height - dh) / 2, dw, dh));
    }

    static void DrawShadow(DrawingContext dc, BackdropSettings s, Rect img)
    {
        if (s.Shadow <= 0) return;
        const int steps = 18;
        double spread = 10 + s.Shadow * 0.7;        // how far the shadow fades out
        double offsetY = spread * 0.35;
        double total = Math.Min(0.85, s.Shadow / 100.0 * 0.9);
        for (int i = 0; i < steps; i++)
        {
            double g = spread * (i + 1) / steps;
            var r = Rect.Inflate(img, g, g);
            r.Offset(0, offsetY);
            double rad = s.InnerRadius + g;
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb((byte)(255 * total / steps * 1.6), 0, 0, 0)), null, r, rad, rad);
        }
    }

    // ---- image sources ----
    static string? cachedPath;
    static BitmapSource? cachedImage;

    static BitmapSource? LoadBackdropImage(BackdropSettings s)
    {
        var path = s.UseWallpaper ? WallpaperPath() : s.ImagePath;
        if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path)) return null;
        if (path == cachedPath && cachedImage != null) return cachedImage;
        try
        {
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.UriSource = new Uri(path);
            bi.DecodePixelWidth = 2200;
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.EndInit();
            bi.Freeze();
            cachedPath = path; cachedImage = bi;
            return bi;
        }
        catch { return null; }
    }

    static string WallpaperPath()
    {
        var sb = new StringBuilder(260);
        Native.SystemParametersInfo(0x0073 /* SPI_GETDESKWALLPAPER */, 260, sb, 0);
        return sb.ToString();
    }

    public static BackdropSettings Randomized(BackdropSettings s)
    {
        var rnd = new Random();
        double h = rnd.NextDouble() * 360, h2 = (h + 30 + rnd.NextDouble() * 70) % 360;
        s.Color1 = ToHex(FromHsv(h, 0.55 + rnd.NextDouble() * 0.35, 0.75 + rnd.NextDouble() * 0.2));
        s.Color2 = ToHex(FromHsv(h2, 0.55 + rnd.NextDouble() * 0.35, 0.75 + rnd.NextDouble() * 0.2));
        s.Angle = rnd.Next(0, 360);
        s.Mode = "gradient";
        return s;
    }

    static Color FromHsv(double h, double sat, double v)
    {
        double c = v * sat, x = c * (1 - Math.Abs(h / 60 % 2 - 1)), m = v - c;
        double r, g, b;
        if (h < 60) { r = c; g = x; b = 0; }
        else if (h < 120) { r = x; g = c; b = 0; }
        else if (h < 180) { r = 0; g = c; b = x; }
        else if (h < 240) { r = 0; g = x; b = c; }
        else if (h < 300) { r = x; g = 0; b = c; }
        else { r = c; g = 0; b = x; }
        return Color.FromRgb((byte)((r + m) * 255), (byte)((g + m) * 255), (byte)((b + m) * 255));
    }
}
