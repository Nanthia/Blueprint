using System;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Shutter;

static class Output
{
    public static bool CopyToClipboard(BitmapSource img)
    {
        var opaque = new FormatConvertedBitmap(img, PixelFormats.Bgr32, null, 0);
        opaque.Freeze();
        var png = new MemoryStream();
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(img));
        enc.Save(png);
        png.Position = 0;
        for (int i = 0; i < 6; i++)
        {
            try
            {
                var data = new DataObject();
                data.SetImage(opaque);
                data.SetData("PNG", png); // keeps transparency (backdrop outer corners) for apps that read PNG
                Clipboard.SetDataObject(data, true);
                return true;
            }
            catch (System.Runtime.InteropServices.COMException) { Thread.Sleep(60); }
        }
        return false;
    }

    public static bool CopyText(string text)
    {
        for (int i = 0; i < 6; i++)
        {
            try { Clipboard.SetText(text); return true; }
            catch (System.Runtime.InteropServices.COMException) { Thread.Sleep(60); }
        }
        return false;
    }

    /// <summary>Start a drag of the image as a PNG file + bitmap from a UI element (preview card, editor).</summary>
    public static void DragOut(DependencyObject source, BitmapSource img)
    {
        try
        {
            var dir = Path.Combine(Path.GetTempPath(), "Shutter");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "SCR-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".png");
            Encode(img, path, false);
            var data = new DataObject();
            data.SetData(DataFormats.FileDrop, new[] { path });
            data.SetImage(new FormatConvertedBitmap(img, PixelFormats.Bgr32, null, 0));
            DragDrop.DoDragDrop(source, data, DragDropEffects.Copy);
        }
        catch { }
    }

    public static BitmapSource? ClipboardImage()
    {
        try
        {
            if (!Clipboard.ContainsImage()) return null;
            var src = Clipboard.GetImage();
            if (src == null) return null;
            var wb = new WriteableBitmap(src);
            wb.Freeze();
            return wb;
        }
        catch { return null; }
    }

    /// <summary>Saves to the default folder. Mirrors the original's 4 attempts: PNG full, JPEG full, PNG reduced, JPEG reduced.</summary>
    public static string? SaveToFolder(BitmapSource img, out string error)
    {
        error = "";
        var s = Settings.Current;
        try { Directory.CreateDirectory(s.Folder); }
        catch (Exception e) { error = "Default folder is not set: " + e.Message; return null; }

        var baseName = "SCR-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
        bool preferJpeg = s.Format == "auto" && (long)img.PixelWidth * img.PixelHeight > 12_000_000;
        var attempts = new (bool jpeg, bool reduced)[]
        {
            (preferJpeg, false), (!preferJpeg, false), (false, true), (true, true),
        };
        foreach (var (jpeg, reduced) in attempts)
        {
            try
            {
                BitmapSource src = img;
                if (reduced)
                {
                    var t = new TransformedBitmap(img, new ScaleTransform(0.5, 0.5));
                    t.Freeze();
                    src = t;
                }
                var path = UniquePath(Path.Combine(s.Folder, baseName + (jpeg ? ".jpg" : ".png")));
                Encode(src, path, jpeg);
                if (File.Exists(path)) return path;
            }
            catch (Exception e) { error = e.Message; }
        }
        return null;
    }

    public static void Encode(BitmapSource img, string path, bool jpeg)
    {
        BitmapEncoder enc = jpeg ? new JpegBitmapEncoder { QualityLevel = 92 } : new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(img));
        using var fs = File.Create(path);
        enc.Save(fs);
    }

    static string UniquePath(string p)
    {
        if (!File.Exists(p)) return p;
        var dir = Path.GetDirectoryName(p)!;
        var name = Path.GetFileNameWithoutExtension(p);
        var ext = Path.GetExtension(p);
        for (int i = 2; ; i++)
        {
            var c = Path.Combine(dir, $"{name}-{i}{ext}");
            if (!File.Exists(c)) return c;
        }
    }
}
