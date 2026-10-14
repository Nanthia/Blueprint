using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Drawing = System.Drawing;

namespace Shutter;

/// <summary>Window and tray icons drawn from the brand geometry (no image files needed at runtime).</summary>
static class AppIcons
{
    static ImageSource? window;

    /// <summary>Tile icon for window title bars / taskbar.</summary>
    public static ImageSource WindowIcon => window ??= Render(64, true);

    public static BitmapSource Render(int size, bool tile)
    {
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen()) ApertureLogo.Draw(dc, size, tile);
        var rtb = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        rtb.Freeze();
        return rtb;
    }

    // ---- tray glyph (16-unit grid from brand/tray-icon-*.svg): six blades with thin gaps ----
    static readonly string[] TrayBlades =
    {
        "M8.00 5.40 L10.25 6.70 L14.87 9.36 A7 7 0 0 0 12.61 2.74 Z",
        "M10.25 6.70 L10.25 9.30 L10.25 14.63 A7 7 0 0 0 14.87 9.36 Z",
        "M10.25 9.30 L8.00 10.60 L3.39 13.26 A7 7 0 0 0 10.25 14.63 Z",
        "M8.00 10.60 L5.75 9.30 L1.13 6.64 A7 7 0 0 0 3.39 13.26 Z",
        "M5.75 9.30 L5.75 6.70 L5.75 1.37 A7 7 0 0 0 1.13 6.64 Z",
        "M5.75 6.70 L8.00 5.40 L12.61 2.74 A7 7 0 0 0 5.75 1.37 Z",
    };

    static Geometry? trayShape;

    static Geometry TrayShape()
    {
        if (trayShape != null) return trayShape;
        Geometry? union = null, gaps = null;
        var pen = new Pen(Brushes.Black, 0.9) { LineJoin = PenLineJoin.Round };
        foreach (var d in TrayBlades)
        {
            var g = Geometry.Parse(d);
            var w = g.GetWidenedPathGeometry(pen);
            union = union == null ? g : Geometry.Combine(union, g, GeometryCombineMode.Union, null);
            gaps = gaps == null ? w : Geometry.Combine(gaps, w, GeometryCombineMode.Union, null);
        }
        trayShape = Geometry.Combine(union!, gaps!, GeometryCombineMode.Exclude, null);
        trayShape.Freeze();
        return trayShape;
    }

    static bool TaskbarIsLight()
    {
        try
        {
            using var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return k?.GetValue("SystemUsesLightTheme") is int v && v == 1;
        }
        catch { return false; }
    }

    /// <summary>The tray icon: white on dark taskbars, near-black on light ones, accent blue while capturing.</summary>
    public static Drawing.Icon Tray(bool capturing)
    {
        const int px = 32;
        Color c = capturing ? Color.FromRgb(0x0A, 0x84, 0xFF) : TaskbarIsLight() ? Color.FromRgb(0x1B, 0x1B, 0x1E) : Colors.White;
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            double s = px / 16.0;
            dc.PushTransform(new ScaleTransform(s, s));
            dc.DrawGeometry(new SolidColorBrush(c), null, TrayShape());
            dc.Pop();
        }
        var rtb = new RenderTargetBitmap(px, px, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        using var ms = new MemoryStream();
        enc.Save(ms);
        ms.Position = 0;
        using var bmp = new Drawing.Bitmap(ms);
        var h = bmp.GetHicon();
        using var tmp = Drawing.Icon.FromHandle(h);
        var owned = (Drawing.Icon)tmp.Clone(); // independent copy so the native handle can be released
        Native.DestroyIcon(h);
        return owned;
    }

    /// <summary>Writes a multi-size .ico (PNG-compressed frames) of the app tile.</summary>
    public static void WriteIco(string path)
    {
        var sizes = new[] { 16, 24, 32, 48, 64, 128, 256 };
        var frames = sizes.Select(sz =>
        {
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(Render(sz, true)));
            using var ms = new MemoryStream();
            enc.Save(ms);
            return (sz, data: ms.ToArray());
        }).ToList();

        using var fs = File.Create(path);
        using var bw = new BinaryWriter(fs);
        bw.Write((short)0); bw.Write((short)1); bw.Write((short)frames.Count);
        int offset = 6 + 16 * frames.Count;
        foreach (var (sz, data) in frames)
        {
            bw.Write((byte)(sz == 256 ? 0 : sz)); bw.Write((byte)(sz == 256 ? 0 : sz));
            bw.Write((byte)0); bw.Write((byte)0);
            bw.Write((short)1); bw.Write((short)32);
            bw.Write(data.Length); bw.Write(offset);
            offset += data.Length;
        }
        foreach (var (_, data) in frames) bw.Write(data);
    }
}
