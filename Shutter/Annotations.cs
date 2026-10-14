using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Shutter;

public sealed class DrawCtx
{
    public required BitmapSource Base { get; init; }
}

public abstract class Ann
{
    public Color Color = Color.FromRgb(0xFF, 0x3B, 0x30);
    public double Size = 4;
    public int FillMode; // 0 none, 1 soft, 2 solid (meaning varies per tool)

    public abstract void Draw(DrawingContext dc, DrawCtx c);
    public abstract Rect Bounds { get; }
    public abstract void Move(Vector v);
    public abstract Ann Clone();
    public virtual bool SupportsFill => false;
    public virtual Point[] HandlePoints => Array.Empty<Point>();
    public virtual void SetHandle(int i, Point p) { }
    public virtual bool Hit(Point p, double tol) => Rect.Inflate(Bounds, tol, tol).Contains(p);

    protected void CopyBaseTo(Ann a) { a.Color = Color; a.Size = Size; a.FillMode = FillMode; }

    protected Pen MakePen(double width, Color? c = null, PenLineCap cap = PenLineCap.Round)
    {
        var p = new Pen(new SolidColorBrush(c ?? Color), width) { LineJoin = PenLineJoin.Round, StartLineCap = cap, EndLineCap = cap };
        p.Freeze();
        return p;
    }

    protected static double DistToSegment(Point p, Point a, Point b)
    {
        var ab = b - a;
        double len2 = ab.LengthSquared;
        if (len2 < 1e-9) return (p - a).Length;
        double t = Math.Clamp(((p - a) * ab) / len2, 0, 1);
        return (p - (a + ab * t)).Length;
    }

    public static Color Contrast(Color c)
    {
        double l = (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255;
        return l > 0.65 ? Colors.Black : Colors.White;
    }
}

public abstract class TwoPointAnn : Ann
{
    public Point P1, P2;
    public override Rect Bounds => new(P1, P2);
    public override void Move(Vector v) { P1 += v; P2 += v; }
    public override Point[] HandlePoints => new[] { P1, P2 };
    public override void SetHandle(int i, Point p) { if (i == 0) P1 = p; else P2 = p; }
    protected T CloneTo<T>(T t) where T : TwoPointAnn { CopyBaseTo(t); t.P1 = P1; t.P2 = P2; return t; }
}

public sealed class LineAnn : TwoPointAnn
{
    public override void Draw(DrawingContext dc, DrawCtx c) => dc.DrawLine(MakePen(Size), P1, P2);
    public override bool Hit(Point p, double tol) => DistToSegment(p, P1, P2) <= tol + Size / 2 + 2;
    public override Ann Clone() => CloneTo(new LineAnn());
}

public sealed class ArrowAnn : TwoPointAnn
{
    public override void Draw(DrawingContext dc, DrawCtx c)
    {
        var d = P2 - P1;
        double len = d.Length;
        if (len < 1) return;
        d.Normalize();
        var n = new Vector(-d.Y, d.X);
        double headW = Size * 2.2 + 5, headL = Math.Min(len * 0.7, Size * 3.5 + 10);
        double tail = Math.Max(0.6, Size * 0.35), neck = Math.Max(1.2, Size * 0.9);
        var neckPt = P2 - d * headL;
        var geo = new StreamGeometry();
        using (var g = geo.Open())
        {
            g.BeginFigure(P1 + n * tail, true, true);
            g.PolyLineTo(new[] { neckPt + n * neck, neckPt + n * headW, P2, neckPt - n * headW, neckPt - n * neck, P1 - n * tail }, true, true);
        }
        geo.Freeze();
        var brush = new SolidColorBrush(Color);
        dc.DrawGeometry(brush, MakePen(1), geo);
    }
    public override bool Hit(Point p, double tol) => DistToSegment(p, P1, P2) <= tol + Size + 6;
    public override Ann Clone() => CloneTo(new ArrowAnn());
}

public sealed class RectAnn : TwoPointAnn
{
    public override bool SupportsFill => true;
    public override void Draw(DrawingContext dc, DrawCtx c)
    {
        Brush? fill = FillMode == 0 ? null : new SolidColorBrush(FillMode == 1 ? Color.FromArgb(0x59, Color.R, Color.G, Color.B) : Color);
        dc.DrawRectangle(fill, MakePen(Size), Bounds);
    }
    public override bool Hit(Point p, double tol) => ShapeHit(Bounds, FillMode > 0, Size, p, tol);
    public override Ann Clone() => CloneTo(new RectAnn());

    internal static bool ShapeHit(Rect b, bool filled, double size, Point p, double tol)
    {
        double band = tol + size / 2 + 1;
        if (!Rect.Inflate(b, band, band).Contains(p)) return false;
        if (filled) return true;
        var inner = Rect.Inflate(b, -band, -band);
        return inner.Width <= 0 || inner.Height <= 0 || !inner.Contains(p);
    }
}

public sealed class OvalAnn : TwoPointAnn
{
    public override bool SupportsFill => true;
    public override void Draw(DrawingContext dc, DrawCtx c)
    {
        var b = Bounds;
        Brush? fill = FillMode == 0 ? null : new SolidColorBrush(FillMode == 1 ? Color.FromArgb(0x59, Color.R, Color.G, Color.B) : Color);
        dc.DrawEllipse(fill, MakePen(Size), new Point(b.X + b.Width / 2, b.Y + b.Height / 2), b.Width / 2, b.Height / 2);
    }
    public override bool Hit(Point p, double tol) => RectAnn.ShapeHit(Bounds, FillMode > 0, Size, p, tol);
    public override Ann Clone() => CloneTo(new OvalAnn());
}

/// <summary>Blur/Erase: pixelates the region (FillMode 0) or erases it with a solid fill (FillMode 1).</summary>
public sealed class BlurAnn : TwoPointAnn
{
    public override bool SupportsFill => true;
    public override void Draw(DrawingContext dc, DrawCtx c)
    {
        var b = Rect.Intersect(Bounds, new Rect(0, 0, c.Base.PixelWidth, c.Base.PixelHeight));
        if (b.IsEmpty || b.Width < 1 || b.Height < 1) return;
        var ir = new Int32Rect((int)b.X, (int)b.Y, Math.Max(1, (int)b.Width), Math.Max(1, (int)b.Height));
        ir.Width = Math.Min(ir.Width, c.Base.PixelWidth - ir.X);
        ir.Height = Math.Min(ir.Height, c.Base.PixelHeight - ir.Y);
        if (FillMode == 1)
        {
            var px = new byte[4];
            new CroppedBitmap(new FormatConvertedBitmap(c.Base, PixelFormats.Bgra32, null, 0), new Int32Rect(ir.X, ir.Y, 1, 1))
                .CopyPixels(px, 4, 0);
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(px[2], px[1], px[0])), null, new Rect(ir.X, ir.Y, ir.Width, ir.Height));
            return;
        }
        double block = Math.Max(4, Size * 2.5);
        var small = new TransformedBitmap(new CroppedBitmap(c.Base, ir), new ScaleTransform(1 / block, 1 / block));
        var dg = new DrawingGroup();
        RenderOptions.SetBitmapScalingMode(dg, BitmapScalingMode.NearestNeighbor);
        using (var dc2 = dg.Open())
            dc2.DrawImage(small, new Rect(ir.X, ir.Y, ir.Width, ir.Height));
        dc.DrawDrawing(dg);
    }
    public override bool Hit(Point p, double tol) => Rect.Inflate(Bounds, tol, tol).Contains(p);
    public override Ann Clone() => CloneTo(new BlurAnn());
}

public sealed class SpotlightAnn : TwoPointAnn
{
    public override void Draw(DrawingContext dc, DrawCtx c)
    {
        var full = new RectangleGeometry(new Rect(0, 0, c.Base.PixelWidth, c.Base.PixelHeight));
        var geo = new CombinedGeometry(GeometryCombineMode.Exclude, full, new RectangleGeometry(Bounds));
        dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(150, 0, 0, 0)), null, geo);
    }
    public override Ann Clone() => CloneTo(new SpotlightAnn());
}

/// <summary>Measuring line with end ticks and a pixel-distance label (the original's ObjRuler; keys 1/2 force an axis).</summary>
public sealed class RulerAnn : TwoPointAnn
{
    public int Axis; // 0 auto-snap to dominant axis, 1 horizontal, 2 vertical

    public Point End => Axis == 1 ? new Point(P2.X, P1.Y)
        : Axis == 2 ? new Point(P1.X, P2.Y)
        : Math.Abs(P2.X - P1.X) >= Math.Abs(P2.Y - P1.Y) ? new Point(P2.X, P1.Y) : new Point(P1.X, P2.Y);

    public override Rect Bounds => new(P1, End);
    public override Point[] HandlePoints => new[] { P1, End };
    public override bool Hit(Point p, double tol) => DistToSegment(p, P1, End) <= tol + 6;
    public override Ann Clone() { var t = CloneTo(new RulerAnn()); t.Axis = Axis; return t; }

    public override void Draw(DrawingContext dc, DrawCtx c)
    {
        var e = End;
        var d = e - P1;
        double len = d.Length;
        if (len < 1) return;
        var dir = d / len;
        var n = new Vector(-dir.Y, dir.X);
        var pen = MakePen(Math.Max(1.5, Size / 2), null, PenLineCap.Flat);
        dc.DrawLine(pen, P1, e);
        dc.DrawLine(pen, P1 + n * 7, P1 - n * 7);
        dc.DrawLine(pen, e + n * 7, e - n * 7);

        var ft = new FormattedText($"{Math.Round(len)} px", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal),
            12, new SolidColorBrush(Contrast(Color)), 1.0);
        var mid = new Point((P1.X + e.X) / 2, (P1.Y + e.Y) / 2);
        bool horizontal = Math.Abs(d.X) >= Math.Abs(d.Y);
        double bx = horizontal ? mid.X - ft.Width / 2 : mid.X + 10;
        double by = horizontal ? mid.Y - ft.Height - 12 : mid.Y - ft.Height / 2;
        if (by < 2) by = mid.Y + 10; // keep the label on-canvas
        var pill = new Rect(bx - 5, by - 1, ft.Width + 10, ft.Height + 2);
        dc.DrawRoundedRectangle(new SolidColorBrush(Color), null, pill, 4, 4);
        dc.DrawText(ft, new Point(bx, by));
    }
}

/// <summary>Circular lens that magnifies the image beneath it (the original's ObjMagnifier). Factor comes from Size.</summary>
public sealed class MagnifierAnn : Ann
{
    public Point Center;
    public double Radius = 40;
    public double Factor => 1 + Size / 3;
    public override Rect Bounds => new(Center.X - Radius, Center.Y - Radius, Radius * 2, Radius * 2);
    public override Point[] HandlePoints => new[] { new Point(Center.X + Radius, Center.Y) };
    public override void SetHandle(int i, Point p) => Radius = Math.Max(8, (p - Center).Length);
    public override bool Hit(Point p, double tol) => (p - Center).Length <= Radius + tol;
    public override void Move(Vector v) => Center += v;
    public override Ann Clone() { var t = new MagnifierAnn { Center = Center, Radius = Radius }; CopyBaseTo(t); return t; }

    public override void Draw(DrawingContext dc, DrawCtx c)
    {
        if (Radius < 2) return;
        double f = Factor;
        var lens = new EllipseGeometry(Center, Radius, Radius);
        dc.PushClip(lens);
        dc.DrawRectangle(Brushes.Black, null, Bounds);
        dc.DrawImage(c.Base, new Rect(Center.X - Center.X * f, Center.Y - Center.Y * f, c.Base.PixelWidth * f, c.Base.PixelHeight * f));
        dc.Pop();
        dc.DrawEllipse(null, new Pen(new SolidColorBrush(Color.FromArgb(120, 0, 0, 0)), 5), Center, Radius, Radius);
        dc.DrawEllipse(null, new Pen(Brushes.White, 3), Center, Radius, Radius);
    }
}

public sealed class TextAnn : Ann
{
    public Point Pos;
    public string Text = "";
    public bool Hidden; // while being edited in place

    public double FontSize => 10 + Size * 1.8;
    public override bool SupportsFill => true;

    FormattedText Format(Color col) => new(Text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
        new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal),
        FontSize, new SolidColorBrush(col), 1.0);

    public override Rect Bounds
    {
        get
        {
            var f = Format(Color);
            double pad = FillMode > 0 ? 6 : 2;
            return new Rect(Pos.X - pad, Pos.Y - pad, Math.Max(8, f.Width) + pad * 2, Math.Max(8, f.Height) + pad * 2);
        }
    }

    public override void Draw(DrawingContext dc, DrawCtx c)
    {
        if (Hidden || string.IsNullOrEmpty(Text)) return;
        if (FillMode > 0)
        {
            dc.DrawRoundedRectangle(new SolidColorBrush(Color), null, Bounds, 5, 5);
            dc.DrawText(Format(Contrast(Color)), Pos);
        }
        else dc.DrawText(Format(Color), Pos);
    }

    public override void Move(Vector v) => Pos += v;
    public override Ann Clone() { var t = new TextAnn { Pos = Pos, Text = Text }; CopyBaseTo(t); return t; }
}

public sealed class CounterAnn : Ann
{
    public Point Center;
    public int Number = 1;
    public double Radius => 9 + Size * 1.2;
    public override Rect Bounds => new(Center.X - Radius, Center.Y - Radius, Radius * 2, Radius * 2);

    public override void Draw(DrawingContext dc, DrawCtx c)
    {
        dc.DrawEllipse(new SolidColorBrush(Color), MakePen(1.5, Colors.White), Center, Radius, Radius);
        var ft = new FormattedText(Number.ToString(), CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
            Radius * 1.15, new SolidColorBrush(Contrast(Color)), 1.0);
        dc.DrawText(ft, new Point(Center.X - ft.Width / 2, Center.Y - ft.Height / 2));
    }

    public override bool Hit(Point p, double tol) => (p - Center).Length <= Radius + tol;
    public override void Move(Vector v) => Center += v;
    public override Ann Clone() { var t = new CounterAnn { Center = Center, Number = Number }; CopyBaseTo(t); return t; }
}

public sealed class FreeAnn : Ann
{
    public List<Point> Points = new();
    public bool Highlighter;
    double Width => Highlighter ? Size * 4 + 4 : Size;

    public override Rect Bounds
    {
        get
        {
            if (Points.Count == 0) return Rect.Empty;
            double x0 = Points.Min(p => p.X), y0 = Points.Min(p => p.Y), x1 = Points.Max(p => p.X), y1 = Points.Max(p => p.Y);
            double h = Width / 2;
            return new Rect(x0 - h, y0 - h, x1 - x0 + Width, y1 - y0 + Width);
        }
    }

    public override void Draw(DrawingContext dc, DrawCtx c)
    {
        if (Points.Count == 0) return;
        var col = Highlighter ? Color.FromArgb(0x66, Color.R, Color.G, Color.B) : Color;
        if (Points.Count == 1)
        {
            dc.DrawEllipse(new SolidColorBrush(col), null, Points[0], Width / 2, Width / 2);
            return;
        }
        var geo = new StreamGeometry();
        using (var g = geo.Open())
        {
            g.BeginFigure(Points[0], false, false);
            for (int i = 1; i < Points.Count - 1; i++)
            {
                var mid = new Point((Points[i].X + Points[i + 1].X) / 2, (Points[i].Y + Points[i + 1].Y) / 2);
                g.QuadraticBezierTo(Points[i], mid, true, true);
            }
            g.LineTo(Points[^1], true, true);
        }
        geo.Freeze();
        dc.DrawGeometry(null, MakePen(Width, col, Highlighter ? PenLineCap.Square : PenLineCap.Round), geo);
    }

    public override bool Hit(Point p, double tol)
    {
        if (Points.Count == 1) return (p - Points[0]).Length <= Width / 2 + tol;
        for (int i = 1; i < Points.Count; i++)
            if (DistToSegment(p, Points[i - 1], Points[i]) <= Width / 2 + tol) return true;
        return false;
    }

    public override void Move(Vector v) { for (int i = 0; i < Points.Count; i++) Points[i] += v; }
    public override Ann Clone() { var t = new FreeAnn { Points = new List<Point>(Points), Highlighter = Highlighter }; CopyBaseTo(t); return t; }
}

/// <summary>Draws the base image plus annotations in image-pixel space (optionally on a backdrop); also renders the flattened result.</summary>
public sealed class AnnotationSurface : FrameworkElement
{
    public BitmapSource Base { get; private set; } = null!;
    public List<Ann> Objects { get; set; } = new();
    public Ann? Selected;
    public Rect? CropRect;
    public BackdropSettings? Backdrop;

    /// <summary>Where the screenshot's top-left sits inside the canvas (non-zero when a backdrop adds padding).</summary>
    public Point Origin { get; private set; }
    public Size CanvasSize { get; private set; }
    public event Action? LayoutChanged;

    BitmapSource? bgCache;
    string? bgKey;

    public AnnotationSurface() { ClipToBounds = true; }

    bool BackdropOn => Backdrop is { Enabled: true };

    public void SetBase(BitmapSource b)
    {
        Base = b;
        RefreshLayout();
    }

    public void RefreshLayout()
    {
        if (BackdropOn)
        {
            var (c, o) = BackdropRenderer.Layout(Backdrop!, Base.PixelWidth, Base.PixelHeight);
            CanvasSize = c; Origin = o;
        }
        else
        {
            CanvasSize = new Size(Base.PixelWidth, Base.PixelHeight);
            Origin = new Point(0, 0);
        }
        Width = CanvasSize.Width;
        Height = CanvasSize.Height;
        bgCache = null;
        InvalidateMeasure();
        InvalidateVisual();
        LayoutChanged?.Invoke();
    }

    protected override Size MeasureOverride(Size availableSize) => CanvasSize;
    protected override void OnRender(DrawingContext dc) => DrawContent(dc, true, true);

    BitmapSource Background()
    {
        var key = $"{Backdrop!.Key()}|{CanvasSize}|{System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(Base)}";
        if (bgCache == null || bgKey != key)
        {
            bgCache = BackdropRenderer.Build(Backdrop!, CanvasSize, new Rect(Origin, new Size(Base.PixelWidth, Base.PixelHeight)), Base);
            bgKey = key;
        }
        return bgCache;
    }

    void DrawContent(DrawingContext dc, bool chrome, bool useBackdrop)
    {
        bool bd = useBackdrop && BackdropOn;
        var canvas = bd ? CanvasSize : new Size(Base.PixelWidth, Base.PixelHeight);
        var origin = bd ? Origin : new Point(0, 0);
        bool clipOuter = bd && Backdrop!.OuterRadius > 0;
        if (clipOuter)
        {
            double r = Backdrop!.OuterRadius;
            dc.PushClip(new RectangleGeometry(new Rect(0, 0, canvas.Width, canvas.Height), r, r));
        }
        if (bd) dc.DrawImage(Background(), new Rect(0, 0, canvas.Width, canvas.Height));

        dc.PushTransform(new TranslateTransform(origin.X, origin.Y));
        var imgRect = new Rect(0, 0, Base.PixelWidth, Base.PixelHeight);
        if (bd && Backdrop!.InnerRadius > 0)
        {
            double ir = Backdrop.InnerRadius;
            dc.PushClip(new RectangleGeometry(imgRect, ir, ir));
            dc.DrawImage(Base, imgRect);
            dc.Pop();
        }
        else dc.DrawImage(Base, imgRect);

        var ctx = new DrawCtx { Base = Base };
        foreach (var o in Objects) o.Draw(dc, ctx);

        if (chrome)
        {
            var accent = new SolidColorBrush(Color.FromRgb(0x0A, 0x84, 0xFF));
            if (Selected != null)
            {
                var b = Selected.Bounds;
                if (!b.IsEmpty)
                {
                    var pen = new Pen(accent, 1) { DashStyle = DashStyles.Dash };
                    dc.DrawRectangle(null, pen, Rect.Inflate(b, 3, 3));
                }
                foreach (var h in Selected.HandlePoints)
                    dc.DrawRectangle(Brushes.White, new Pen(accent, 1.5), new Rect(h.X - 4, h.Y - 4, 8, 8));
            }
            if (CropRect is { } cr && cr.Width > 0 && cr.Height > 0)
            {
                var full = new RectangleGeometry(imgRect);
                dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(150, 0, 0, 0)), null,
                    new CombinedGeometry(GeometryCombineMode.Exclude, full, new RectangleGeometry(cr)));
                dc.DrawRectangle(null, new Pen(Brushes.White, 1.5), cr);
            }
        }
        dc.Pop(); // translate
        if (clipOuter) dc.Pop();
    }

    /// <summary>Flattened output. withBackdrop=false renders just the screenshot + annotations.</summary>
    public BitmapSource Render(bool withBackdrop = true)
    {
        bool bd = withBackdrop && BackdropOn;
        int w = bd ? (int)CanvasSize.Width : Base.PixelWidth, h = bd ? (int)CanvasSize.Height : Base.PixelHeight;
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen()) DrawContent(dc, false, withBackdrop);
        var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        rtb.Freeze();
        return rtb;
    }
}
