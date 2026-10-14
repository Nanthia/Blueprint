using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Drawing = System.Drawing;

namespace Blueprint;

/// <summary>Window and tray icons drawn from the bunny brand geometry (no image files needed at runtime).</summary>
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

    // ---- tray glyph: bunny silhouette on a 16-unit grid (brand/bunny/bunny-tray-*.svg), eyes and nose cut out ----
    static readonly string[] TrayShapes =
    {
        "M4.6 7.4C3.7 5.4 3.6 2.6 4.6 1.6c1-.9 2.1.5 2.2 2.8.1 1.3-.1 2.3-.4 3z",
        "M9.1 7.4C8.8 5.6 9 3.8 10 3.1c1-.7 2.6-.3 3.4 1 .4.6 0 1.2-.7.9-.8-.4-1.5-.6-1.9-.1-.4.6-.3 1.6-.2 2.6z",
        "M8 6.6c3.4 0 5.7 1.8 5.7 4.4 0 2.4-2.4 3.6-5.7 3.6s-5.7-1.2-5.7-3.6c0-2.6 2.3-4.4 5.7-4.4z",
    };

    static Geometry? trayShape;

    static Geometry TrayShape()
    {
        if (trayShape != null) return trayShape;
        Geometry? union = null;
        foreach (var d in TrayShapes)
        {
            var g = Geometry.Parse(d);
            union = union == null ? g : Geometry.Combine(union, g, GeometryCombineMode.Union, null);
        }
        Geometry holes = new EllipseGeometry(new Point(5.1, 10.2), .95, .95);
        holes = Geometry.Combine(holes, new EllipseGeometry(new Point(10.9, 9.9), .95, .95), GeometryCombineMode.Union, null);
        holes = Geometry.Combine(holes, Geometry.Parse("M7.3 10.6h1.5L8 11.5z"), GeometryCombineMode.Union, null);
        trayShape = Geometry.Combine(union!, holes, GeometryCombineMode.Exclude, null);
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
