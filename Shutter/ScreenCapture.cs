using System;
using Imaging = System.Drawing.Imaging;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace Shutter;

static class ScreenCapture
{
    /// <summary>Captures a physical-pixel rectangle of the virtual screen.</summary>
    public static BitmapSource Grab(Drawing.Rectangle r)
    {
        using var bmp = new Drawing.Bitmap(r.Width, r.Height, Imaging.PixelFormat.Format32bppRgb);
        using (var g = Drawing.Graphics.FromImage(bmp))
        {
            // BitBlt with CAPTUREBLT so layered/transparent windows are included (CopyFromScreen rejects that flag combo)
            var screen = Native.GetDC(IntPtr.Zero);
            var dest = g.GetHdc();
            try { Native.BitBlt(dest, 0, 0, r.Width, r.Height, screen, r.X, r.Y, 0x00CC0020 | 0x40000000); }
            finally { g.ReleaseHdc(dest); Native.ReleaseDC(IntPtr.Zero, screen); }
        }
        return ToSource(bmp);
    }

    /// <summary>Raw BGRA pixels of a physical-pixel rectangle (used by scrolling capture).</summary>
    public static (byte[] bgra, int w, int h) GrabBytes(Drawing.Rectangle r)
    {
        using var bmp = new Drawing.Bitmap(r.Width, r.Height, Imaging.PixelFormat.Format32bppRgb);
        using (var g = Drawing.Graphics.FromImage(bmp))
        {
            var screen = Native.GetDC(IntPtr.Zero);
            var dest = g.GetHdc();
            try { Native.BitBlt(dest, 0, 0, r.Width, r.Height, screen, r.X, r.Y, 0x00CC0020 | 0x40000000); }
            finally { g.ReleaseHdc(dest); Native.ReleaseDC(IntPtr.Zero, screen); }
        }
        var d = bmp.LockBits(new Drawing.Rectangle(0, 0, bmp.Width, bmp.Height), Imaging.ImageLockMode.ReadOnly, Imaging.PixelFormat.Format32bppRgb);
        try
        {
            var buf = new byte[d.Stride * bmp.Height];
            System.Runtime.InteropServices.Marshal.Copy(d.Scan0, buf, 0, buf.Length);
            return (buf, bmp.Width, bmp.Height);
        }
        finally { bmp.UnlockBits(d); }
    }

    public static BitmapSource GrabCursorScreen() => Grab(Forms.Screen.FromPoint(Forms.Cursor.Position).Bounds);

    public static (Drawing.Rectangle rect, BitmapSource img)? GrabActiveWindow()
    {
        var h = Native.GetForegroundWindow();
        if (h == IntPtr.Zero) return null;
        if (Native.DwmGetWindowAttribute(h, 9 /* DWMWA_EXTENDED_FRAME_BOUNDS */, out var r, System.Runtime.InteropServices.Marshal.SizeOf<Native.RECT>()) != 0
            && !Native.GetWindowRect(h, out r)) return null;
        var rect = Drawing.Rectangle.Intersect(Drawing.Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom), Forms.SystemInformation.VirtualScreen);
        if (rect.Width < 2 || rect.Height < 2) return null;
        return (rect, Grab(rect));
    }

    /// <summary>Frame bounds (physical px) of the top-most visible window under a screen point, skipping our own windows.</summary>
    public static (Drawing.Rectangle rect, string title)? WindowAt(Drawing.Point pt)
    {
        (Drawing.Rectangle rect, string title)? found = null;
        var titleBuf = new System.Text.StringBuilder(256);
        uint me = (uint)Environment.ProcessId;
        var cls = new System.Text.StringBuilder(64);
        Native.EnumWindows((h, _) =>
        {
            if (!Native.IsWindowVisible(h) || Native.IsIconic(h)) return true;
            Native.GetWindowThreadProcessId(h, out var pid);
            if (pid == me) return true;
            if (Native.DwmGetWindowAttributeInt(h, 14 /* DWMWA_CLOAKED */, out var cloaked, 4) == 0 && cloaked != 0) return true;
            cls.Clear();
            Native.GetClassName(h, cls, 64);
            var c = cls.ToString();
            if (c is "Progman" or "WorkerW") return true;
            if (Native.DwmGetWindowAttribute(h, 9, out var r, System.Runtime.InteropServices.Marshal.SizeOf<Native.RECT>()) != 0
                && !Native.GetWindowRect(h, out r)) return true;
            var rect = Drawing.Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom);
            if (rect.Width < 4 || rect.Height < 4 || !rect.Contains(pt)) return true;
            titleBuf.Clear();
            Native.GetWindowText(h, titleBuf, 256);
            found = (rect, titleBuf.ToString());
            return false;
        }, IntPtr.Zero);
        return found;
    }

    static BitmapSource ToSource(Drawing.Bitmap bmp)
    {
        var d = bmp.LockBits(new Drawing.Rectangle(0, 0, bmp.Width, bmp.Height), Imaging.ImageLockMode.ReadOnly, Imaging.PixelFormat.Format32bppRgb);
        try
        {
            var bs = BitmapSource.Create(bmp.Width, bmp.Height, 96, 96, PixelFormats.Bgr32, null, d.Scan0, d.Stride * bmp.Height, d.Stride);
            bs.Freeze();
            return bs;
        }
        finally { bmp.UnlockBits(d); }
    }
}
