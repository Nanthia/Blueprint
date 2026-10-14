using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using ZXing;
using ZXing.Common;

namespace Blueprint;

/// <param name="Kind">qr | text | none | unavailable | error</param>
public sealed record OcrOutcome(string Kind, string? Text, string? Error = null);

/// <summary>"Recognize Text/QR": QR first (ZXing), then Windows' built-in OCR engine.</summary>
public static class Ocr
{
    public static List<string> AvailableLanguages()
    {
        try { return OcrEngine.AvailableRecognizerLanguages.Select(l => l.LanguageTag).ToList(); }
        catch { return new List<string>(); }
    }

    static OcrEngine? CreateEngine()
    {
        var tag = Settings.Current.OcrLanguage;
        if (!string.IsNullOrEmpty(tag) && tag != "auto")
        {
            try { var e = OcrEngine.TryCreateFromLanguage(new Language(tag)); if (e != null) return e; }
            catch { }
        }
        return OcrEngine.TryCreateFromUserProfileLanguages();
    }

    public static async Task<OcrOutcome> RecognizeAsync(BitmapSource img)
    {
        try
        {
            var (pixels, w, h) = ToBgra(img);
            var qr = TryQr(pixels, w, h);
            if (qr != null) return new OcrOutcome("qr", qr);

            var engine = CreateEngine();
            if (engine == null)
                return new OcrOutcome("unavailable", null, "OCR is not ready: no OCR language is installed (Windows Settings > Time & language > Language).");

            var prepared = Prepare(img, OcrEngine.MaxImageDimension);
            using var sb = new SoftwareBitmap(BitmapPixelFormat.Bgra8, prepared.w, prepared.h, BitmapAlphaMode.Premultiplied);
            sb.CopyFromBuffer(prepared.pixels.AsBuffer());
            var result = await engine.RecognizeAsync(sb);

            var lines = result.Lines.Select(l => l.Text.Trim()).Where(t => t.Length > 0).ToList();
            if (lines.Count == 0) return new OcrOutcome("none", null);
            var text = Settings.Current.RemoveBreaks ? string.Join(" ", lines) : string.Join(Environment.NewLine, lines);
            return new OcrOutcome("text", text);
        }
        catch (Exception ex)
        {
            return new OcrOutcome("error", null, ex.Message);
        }
    }

    /// <summary>Plain recognised text (no QR step, nothing copied) — used to index screenshots for library search.</summary>
    public static async Task<string> TextOnlyAsync(BitmapSource img)
    {
        try
        {
            var engine = CreateEngine();
            if (engine == null) return "";
            var prepared = Prepare(img, OcrEngine.MaxImageDimension);
            using var sb = new SoftwareBitmap(BitmapPixelFormat.Bgra8, prepared.w, prepared.h, BitmapAlphaMode.Premultiplied);
            sb.CopyFromBuffer(prepared.pixels.AsBuffer());
            var result = await engine.RecognizeAsync(sb);
            return string.Join(" ", result.Lines.Select(l => l.Text.Trim()).Where(t => t.Length > 0));
        }
        catch { return ""; }
    }

    /// <summary>Puts the recognised text on the clipboard and returns a short message for the UI.</summary>
    public static string ApplyToClipboard(OcrOutcome o)
    {
        switch (o.Kind)
        {
            case "qr":
                Output.CopyText(o.Text!);
                return "QR code copied: " + Trim(o.Text!);
            case "text":
                Output.CopyText(o.Text!);
                return $"Copied {o.Text!.Length} characters: " + Trim(o.Text!);
            case "none": return "No text detected";
            case "unavailable": return o.Error ?? "OCR is not ready";
            default: return "OCR failed: " + o.Error;
        }
    }

    static string Trim(string s)
    {
        s = s.Replace("\r", " ").Replace("\n", " ");
        return s.Length > 70 ? s[..70] + "…" : s;
    }

    static (byte[] pixels, int w, int h) ToBgra(BitmapSource src)
    {
        var conv = new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);
        int w = conv.PixelWidth, h = conv.PixelHeight, stride = w * 4;
        var px = new byte[stride * h];
        conv.CopyPixels(px, stride, 0);
        return (px, w, h);
    }

    static string? TryQr(byte[] bgra, int w, int h)
    {
        try
        {
            var reader = new BarcodeReaderGeneric
            {
                AutoRotate = true,
                Options = new DecodingOptions { TryHarder = true, PossibleFormats = new List<BarcodeFormat> { BarcodeFormat.QR_CODE } },
            };
            var result = reader.Decode(new RGBLuminanceSource(bgra, w, h, RGBLuminanceSource.BitmapFormat.BGRA32));
            return string.IsNullOrWhiteSpace(result?.Text) ? null : result.Text;
        }
        catch { return null; }
    }

    /// <summary>Upscale small captures (the engine reads small text poorly) and invert light-on-dark images.</summary>
    static (byte[] pixels, int w, int h) Prepare(BitmapSource src, uint maxDim)
    {
        BitmapSource work = src;
        int w = src.PixelWidth, h = src.PixelHeight;
        double scale = 1;
        if ((long)w * h < 800 * 600) scale = 2;
        if (w * scale > maxDim || h * scale > maxDim) scale = Math.Min((double)maxDim / w, (double)maxDim / h);
        if (Math.Abs(scale - 1) > 0.01)
        {
            int nw = Math.Max(1, (int)(w * scale)), nh = Math.Max(1, (int)(h * scale));
            var dv = new DrawingVisual();
            RenderOptions.SetBitmapScalingMode(dv, BitmapScalingMode.HighQuality);
            using (var dc = dv.RenderOpen()) dc.DrawImage(src, new Rect(0, 0, nw, nh));
            var rtb = new RenderTargetBitmap(nw, nh, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            work = rtb;
        }
        var (px, pw, ph) = ToBgra(work);

        // mean luminance (sampled); dark backgrounds -> invert so text is dark-on-light
        long sum = 0; int n = 0;
        for (int i = 0; i < px.Length; i += 16) { sum += (px[i + 2] * 299 + px[i + 1] * 587 + px[i] * 114) / 1000; n++; }
        if (n > 0 && sum / n < 110)
            for (int i = 0; i < px.Length; i += 4) { px[i] = (byte)(255 - px[i]); px[i + 1] = (byte)(255 - px[i + 1]); px[i + 2] = (byte)(255 - px[i + 2]); }
        for (int i = 3; i < px.Length; i += 4) px[i] = 255;
        return (px, pw, ph);
    }
}
