// Drawing kit for make_icons.ps1. Compiled by Windows PowerShell's Add-Type, so this is C# 5 (no $"", ?. or => members).
// Icons are drawn in a 256 x 256 logical space at 4x, then scaled down, so every edge is anti-aliased.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

/// <summary>A three-step ramp: light (top left), mid, dark (the cel shadow).</summary>
public class Pal
{
    public Color L, M, D;
    public Pal(string l, string m, string d) { L = Hex(l); M = Hex(m); D = Hex(d); }

    public static Color Hex(string s) { return ColorTranslator.FromHtml(s); }

    // Trait colors from Assets/ArtStyleTest/CLAUDE.md: a trait owns its color
    public static readonly Pal Fire = new Pal("#FFD24A", "#FF8A2A", "#D2451A");
    public static readonly Pal Frost = new Pal("#F2FDFF", "#BFF3FF", "#5FBEDF");
    public static readonly Pal Poison = new Pal("#DDFF8A", "#9CFF4A", "#46A82E");
    public static readonly Pal Lightning = new Pal("#FFFBC8", "#FFE85A", "#E0A21E");
    public static readonly Pal Steel = new Pal("#D5DAE2", "#A8AEBA", "#646A78");
    public static readonly Pal Arcane = new Pal("#EAD9FF", "#C9A2FF", "#8050CC");
    public static readonly Pal Mirror = new Pal("#FFFFFF", "#E9F1FA", "#A6B4CC");
    public static readonly Pal Summon = new Pal("#FFF2C0", "#FFE08A", "#D69C32");
    // Free accents
    public static readonly Pal Rose = new Pal("#FFC6D6", "#F2789C", "#B83F6C");
    public static readonly Pal Teal = new Pal("#A8F0E4", "#3FC4B4", "#1C7F7C");
    public static readonly Pal Ruby = new Pal("#FF8C94", "#D92F45", "#8A1530");
    public static readonly Pal Sapphire = new Pal("#9CC4FF", "#3F74E0", "#203E9C");
    public static readonly Pal Candle = new Pal("#FFEFC4", "#FFC870", "#D48A3A");
    // Materials
    public static readonly Pal Iron = new Pal("#AEB4C0", "#7A808E", "#464B58");
    public static readonly Pal Gold = new Pal("#FFEBA0", "#EEBB4A", "#A8701E");
    public static readonly Pal Silver = new Pal("#FFFFFF", "#C8D0DC", "#7E8898");
    public static readonly Pal Bronze = new Pal("#F0BE80", "#BC7A3E", "#74441F");
    public static readonly Pal Copper = new Pal("#FFC09A", "#D47A4E", "#843E28");
    public static readonly Pal Leather = new Pal("#C89468", "#9A6840", "#5E3A24");
    public static readonly Pal DarkLeather = new Pal("#8C6A58", "#624638", "#3A2822");
    public static readonly Pal Wood = new Pal("#D4A070", "#A67448", "#6A4428");
    public static readonly Pal Cloth = new Pal("#E8DCC4", "#C8B898", "#8E7C64");
    public static readonly Pal Linen = new Pal("#8FA8C8", "#5E7CA6", "#3A5078");
    public static readonly Pal Plum = new Pal("#A884C0", "#745490", "#483260");
    public static readonly Pal Moss = new Pal("#A8B078", "#747E4E", "#4A5234");
    public static readonly Pal Bone = new Pal("#FFF8E4", "#E6D8B8", "#A8946E");
    public static readonly Pal Stone = new Pal("#B4B0AC", "#85807E", "#54504E");
    public static readonly Pal Shadow = new Pal("#6E6288", "#463C60", "#28223C");
    public static readonly Pal Blood = new Pal("#F0606A", "#B8203A", "#6E1028");
    public static readonly Pal Flesh = new Pal("#FFD8B8", "#F0B088", "#BC7C5C");
    public static readonly Pal Jade = new Pal("#B0F0C4", "#4CB880", "#1E7458");
}

/// <summary>Path builders. All paths are closed and fillable; coordinates are in the 256 x 256 logical space.</summary>
public static class G
{
    static PointF[] Pts(float[] xy)
    {
        var p = new PointF[xy.Length / 2];
        for (int i = 0; i < p.Length; i++) p[i] = new PointF(xy[i * 2], xy[i * 2 + 1]);
        return p;
    }

    static GraphicsPath New() { return new GraphicsPath(FillMode.Winding); }

    /// <summary>Sharp polygon.</summary>
    public static GraphicsPath Poly(params float[] xy)
    {
        var p = New(); p.AddPolygon(Pts(xy)); return p;
    }

    /// <summary>Polygon with its corners cut twice (Chaikin): chunky, soft and cozy.</summary>
    public static GraphicsPath Soft(params float[] xy)
    {
        var pts = new List<PointF>(Pts(xy));
        for (int k = 0; k < 3; k++)
        {
            var next = new List<PointF>();
            for (int i = 0; i < pts.Count; i++)
            {
                PointF a = pts[i], b = pts[(i + 1) % pts.Count];
                next.Add(new PointF(a.X * .75f + b.X * .25f, a.Y * .75f + b.Y * .25f));
                next.Add(new PointF(a.X * .25f + b.X * .75f, a.Y * .25f + b.Y * .75f));
            }
            pts = next;
        }
        var p = New(); p.AddPolygon(pts.ToArray()); return p;
    }

    /// <summary>Polygon with each corner rounded to about radius r: straight sides, soft corners.</summary>
    public static GraphicsPath Round(float r, params float[] xy)
    {
        var pts = Pts(xy);
        var p = New();
        for (int i = 0; i < pts.Length; i++)
        {
            PointF a = pts[(i + pts.Length - 1) % pts.Length], b = pts[i], c = pts[(i + 1) % pts.Length];
            PointF p1 = Toward(b, a, r), p2 = Toward(b, c, r);
            p.AddBezier(p1, Mix(p1, b, .6f), Mix(p2, b, .6f), p2);
        }
        p.CloseFigure();
        return p;
    }

    static PointF Toward(PointF from, PointF to, float distance)
    {
        float dx = to.X - from.X, dy = to.Y - from.Y, length = (float)Math.Sqrt(dx * dx + dy * dy);
        float d = Math.Min(distance, length / 2) / length;
        return new PointF(from.X + dx * d, from.Y + dy * d);
    }

    static PointF Mix(PointF a, PointF b, float t) { return new PointF(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t); }

    /// <summary>An oval band, as a ring seen from above.</summary>
    public static GraphicsPath EllRing(float x, float y, float rx, float ry, float thickness)
    {
        var p = new GraphicsPath(FillMode.Alternate);
        p.AddEllipse(x - rx, y - ry, rx * 2, ry * 2);
        p.AddEllipse(x - rx + thickness, y - ry + thickness * .7f, (rx - thickness) * 2, (ry - thickness * .7f) * 2);
        return p;
    }

    /// <summary>The top half of an oval with a gently curved foot.</summary>
    public static GraphicsPath Dome(float x, float y, float rx, float ry)
    {
        var p = New();
        p.AddArc(x - rx, y - ry, rx * 2, ry * 2, 180, 180);
        p.AddBezier(x + rx, y, x + rx * .5f, y + ry * .32f, x - rx * .5f, y + ry * .32f, x - rx, y);
        p.CloseFigure();
        return p;
    }

    /// <summary>Closed smooth curve through the points.</summary>
    public static GraphicsPath Blob(params float[] xy)
    {
        var p = New(); p.AddClosedCurve(Pts(xy), .5f); return p;
    }

    public static GraphicsPath Circle(float x, float y, float r) { return Ell(x, y, r, r); }

    public static GraphicsPath Ell(float x, float y, float rx, float ry)
    {
        var p = New(); p.AddEllipse(x - rx, y - ry, rx * 2, ry * 2); return p;
    }

    /// <summary>Rounded rectangle from its top left corner.</summary>
    public static GraphicsPath RR(float x, float y, float w, float h, float r)
    {
        r = Math.Min(r, Math.Min(w, h) / 2f);
        var p = New();
        p.AddArc(x, y, r * 2, r * 2, 180, 90);
        p.AddArc(x + w - r * 2, y, r * 2, r * 2, 270, 90);
        p.AddArc(x + w - r * 2, y + h - r * 2, r * 2, r * 2, 0, 90);
        p.AddArc(x, y + h - r * 2, r * 2, r * 2, 90, 90);
        p.CloseFigure();
        return p;
    }

    /// <summary>Regular star or, with inner == outer, a polygon. Rotation in degrees, 0 is a point straight up.</summary>
    public static GraphicsPath Star(float x, float y, float outer, float inner, int points, float rot)
    {
        var xy = new float[points * 4];
        for (int i = 0; i < points * 2; i++)
        {
            double a = (rot - 90 + i * 180.0 / points) * Math.PI / 180;
            float r = i % 2 == 0 ? outer : inner;
            xy[i * 2] = x + (float)Math.Cos(a) * r;
            xy[i * 2 + 1] = y + (float)Math.Sin(a) * r;
        }
        return Poly(xy);
    }

    /// <summary>A donut.</summary>
    public static GraphicsPath Ring(float x, float y, float outer, float inner)
    {
        return Arc(x, y, (outer + inner) / 2, outer - inner, 0, 360);
    }

    /// <summary>A thick arc with round ends. Angles in degrees, clockwise from 3 o'clock.</summary>
    public static GraphicsPath Arc(float x, float y, float r, float width, float start, float sweep)
    {
        var line = New();
        if (Math.Abs(sweep) >= 360) { line.AddEllipse(x - r, y - r, r * 2, r * 2); }
        else line.AddArc(x - r, y - r, r * 2, r * 2, start, sweep);
        using (var pen = new Pen(Color.Black, width))
        {
            pen.StartCap = LineCap.Round; pen.EndCap = LineCap.Round; pen.LineJoin = LineJoin.Round;
            line.Widen(pen, null, .05f);
        }
        line.FillMode = Math.Abs(sweep) >= 360 ? FillMode.Alternate : FillMode.Winding;
        return line;
    }

    /// <summary>A thick smooth line through the points, as a fillable shape.</summary>
    public static GraphicsPath Rope(float width, params float[] xy)
    {
        var line = New();
        var pts = Pts(xy);
        if (pts.Length == 2) line.AddLine(pts[0], pts[1]); else line.AddCurve(pts, .5f);
        using (var pen = new Pen(Color.Black, width))
        {
            pen.StartCap = LineCap.Round; pen.EndCap = LineCap.Round; pen.LineJoin = LineJoin.Round;
            line.Widen(pen, null, .05f);
        }
        line.FillMode = FillMode.Winding;
        return line;
    }

    /// <summary>A thick straight-jointed line, as a fillable shape.</summary>
    public static GraphicsPath Bar(float width, params float[] xy)
    {
        var line = New();
        line.AddLines(Pts(xy));
        using (var pen = new Pen(Color.Black, width))
        {
            pen.StartCap = LineCap.Round; pen.EndCap = LineCap.Round; pen.LineJoin = LineJoin.Round;
            line.Widen(pen, null, .05f);
        }
        line.FillMode = FillMode.Winding;
        return line;
    }

    /// <summary>Teardrop with its point up. r is the bulb radius.</summary>
    public static GraphicsPath Drop(float x, float y, float r)
    {
        var p = New();
        p.AddBezier(x, y - r * 2f, x + r * .35f, y - r * 1.1f, x + r * 1.05f, y - r * .6f, x + r, y + r * .05f);
        p.AddBezier(x + r, y + r * .05f, x + r, y + r * 1.25f, x - r, y + r * 1.25f, x - r, y + r * .05f);
        p.AddBezier(x - r, y + r * .05f, x - r * 1.05f, y - r * .6f, x - r * .35f, y - r * 1.1f, x, y - r * 2f);
        p.CloseFigure();
        return p;
    }

    public static GraphicsPath Heart(float x, float y, float r)
    {
        var p = New();
        p.AddBezier(x, y + r * 1.05f, x - r * 1.5f, y + r * .1f, x - r * 1.3f, y - r * 1.05f, x - r * .55f, y - r * .95f);
        p.AddBezier(x - r * .55f, y - r * .95f, x - r * .2f, y - r * .95f, x, y - r * .7f, x, y - r * .45f);
        p.AddBezier(x, y - r * .45f, x, y - r * .7f, x + r * .2f, y - r * .95f, x + r * .55f, y - r * .95f);
        p.AddBezier(x + r * .55f, y - r * .95f, x + r * 1.3f, y - r * 1.05f, x + r * 1.5f, y + r * .1f, x, y + r * 1.05f);
        p.CloseFigure();
        return p;
    }

    /// <summary>A flame with three licks, base at the bottom. x is the center, y the base, w and h its size.</summary>
    public static GraphicsPath Flame(float x, float y, float w, float h)
    {
        float l = x - w / 2, r = x + w / 2, t = y - h;
        var p = New();
        p.AddBezier(x, y, l - w * .08f, y, l - w * .05f, y - h * .42f, l + w * .16f, y - h * .62f);
        p.AddBezier(l + w * .16f, y - h * .62f, l + w * .2f, y - h * .5f, l + w * .28f, y - h * .46f, l + w * .34f, y - h * .5f);
        p.AddBezier(l + w * .34f, y - h * .5f, l + w * .3f, y - h * .72f, x - w * .05f, y - h * .84f, x + w * .04f, t);
        p.AddBezier(x + w * .04f, t, x + w * .08f, y - h * .78f, x + w * .2f, y - h * .72f, r - w * .14f, y - h * .66f);
        p.AddBezier(r - w * .14f, y - h * .66f, r - w * .12f, y - h * .6f, r - w * .14f, y - h * .52f, r - w * .12f, y - h * .5f);
        p.AddBezier(r - w * .12f, y - h * .5f, r + w * .12f, y - h * .3f, r + w * .06f, y, x, y);
        p.CloseFigure();
        return p;
    }

    /// <summary>A jagged lightning bolt from the top right down to the bottom left, inside the box.</summary>
    public static GraphicsPath Bolt(float x, float y, float w, float h)
    {
        return Poly(x + w * .62f, y, x + w * .08f, y + h * .56f, x + w * .44f, y + h * .56f,
                    x + w * .22f, y + h, x + w * .94f, y + h * .38f, x + w * .55f, y + h * .38f, x + w * .86f, y);
    }

    /// <summary>A crescent opening to the right, then rotated. thickness 0 to 1 of the radius.</summary>
    public static GraphicsPath Crescent(float x, float y, float r, float thickness, float rot)
    {
        var xy = new List<float>();
        const int n = 24;
        for (int i = 0; i <= n; i++)
        {
            double a = (60 + i * 240.0 / n) * Math.PI / 180;
            xy.Add((float)Math.Cos(a) * r); xy.Add((float)Math.Sin(a) * r);
        }
        float cut = r * (1 - thickness) * 1.15f;
        float ex = (float)Math.Cos(60 * Math.PI / 180) * r, ey = (float)Math.Sin(60 * Math.PI / 180) * r;
        for (int i = 1; i < n; i++)
        {
            float t = (float)i / n;
            float yy = -ey + t * 2 * ey;
            float bulge = 1 - (yy / ey) * (yy / ey);
            xy.Add(ex - cut * bulge); xy.Add(yy);
        }
        var p = Poly(xy.ToArray());
        return Xf(p, rot, 0, 0, x, y);
    }

    /// <summary>Rotates about (cx, cy), then moves by (dx, dy). Changes and returns the same path.</summary>
    public static GraphicsPath Xf(GraphicsPath p, float rot, float cx, float cy, float dx, float dy)
    {
        using (var m = new Matrix())
        {
            m.Translate(dx, dy);
            m.RotateAt(rot, new PointF(cx, cy));
            p.Transform(m);
        }
        return p;
    }

    public static GraphicsPath Rot(GraphicsPath p, float deg, float cx, float cy) { return Xf(p, deg, cx, cy, 0, 0); }

    public static GraphicsPath Move(GraphicsPath p, float dx, float dy) { return Xf(p, 0, 0, 0, dx, dy); }

    /// <summary>A mirrored copy about the vertical line x = cx.</summary>
    public static GraphicsPath Mirror(GraphicsPath p, float cx)
    {
        var c = (GraphicsPath)p.Clone();
        using (var m = new Matrix(-1, 0, 0, 1, cx * 2, 0)) c.Transform(m);
        return c;
    }

    public static GraphicsPath Add(params GraphicsPath[] parts)
    {
        var p = New();
        foreach (var part in parts) p.AddPath(part, false);
        return p;
    }
}

/// <summary>A queued shape: filled, cel shaded, then decorated inside its own outline.</summary>
public class Part
{
    public GraphicsPath Path; public Pal C;
    public float Line = 5f;       // Inner ink line width (0: none)
    public float Shade = 1f;      // Cel shadow and rim strength (0: flat)
    public Action Decor;          // Runs clipped to the shape, before the ink line
    public Part Flat() { Shade = 0f; return this; }
    public Part NoLine() { Line = 0f; return this; }
    public Part Thin() { Line = 3.5f; return this; }
    public Part Then(Action decor) { Decor = decor; return this; }
}

/// <summary>One icon being drawn.</summary>
public class Ic : IDisposable
{
    public const int Size = 256, SS = 4;
    public static readonly Color Ink = Pal.Hex("#1A1218");

    readonly Bitmap _bmp;
    public readonly Graphics G2;
    readonly List<Part> _queue = new List<Part>();
    float _rot;

    /// <summary>Half the width of the ink line around a group's whole silhouette.</summary>
    public float Outer = 7f;

    public Ic()
    {
        _bmp = new Bitmap(Size * SS, Size * SS, PixelFormat.Format32bppPArgb);
        G2 = Graphics.FromImage(_bmp);
        G2.SmoothingMode = SmoothingMode.AntiAlias;
        G2.PixelOffsetMode = PixelOffsetMode.HighQuality;
        G2.CompositingQuality = CompositingQuality.HighQuality;
        Xf(0, 128, 128, 1);
    }

    public void Dispose() { G2.Dispose(); _bmp.Dispose(); }

    /// <summary>Rotates and scales what is flushed next about (cx, cy). Light stays top left.</summary>
    public void Xf(float rot, float cx, float cy, float scale)
    {
        _rot = rot;
        G2.ResetTransform();
        G2.ScaleTransform(SS, SS);
        G2.TranslateTransform(cx, cy);
        G2.RotateTransform(rot);
        G2.ScaleTransform(scale, scale);
        G2.TranslateTransform(-cx, -cy);
    }

    public void NoXf() { Flush(); Xf(0, 128, 128, 1); }

    /// <summary>The soft rounded background: a dark, tinted tile lit from the middle.</summary>
    public void Tile(string edge, string center)
    {
        var path = G.RR(5, 5, 246, 246, 46);
        var e = Pal.Hex(edge); var m = Pal.Hex(center);
        using (var b = new PathGradientBrush(path))
        {
            b.CenterPoint = new PointF(118, 106);
            b.CenterColor = m;
            b.SurroundColors = new[] { e };
            b.FocusScales = new PointF(.28f, .28f);
            G2.FillPath(b, path);
        }
        // A soft lit top edge and a darker foot, like a pressed clay button
        var keep = G2.Save();
        G2.SetClip(path);
        using (var light = new Pen(Color.FromArgb(46, 255, 244, 224), 4f)) G2.DrawPath(light, G.Move(G.RR(5, 5, 246, 246, 46), 0, 2));
        using (var dark = new Pen(Color.FromArgb(70, 10, 6, 14), 9f)) G2.DrawPath(dark, G.Move(G.RR(5, 5, 246, 246, 46), 0, -3));
        G2.Restore(keep);
        using (var pen = new Pen(Color.FromArgb(150, Ink), 3f)) G2.DrawPath(pen, path);
    }

    public Part Add(GraphicsPath path, Pal c)
    {
        var part = new Part { Path = path, C = c };
        _queue.Add(part);
        return part;
    }

    /// <summary>Draws the queue: one thick ink silhouette around everything, then each shape on top.</summary>
    public void Flush()
    {
        if (_queue.Count == 0) return;
        using (var pen = new Pen(Ink, Outer * 2) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round })
        using (var ink = new SolidBrush(Ink))
            foreach (var part in _queue)
            {
                G2.DrawPath(pen, part.Path);
                G2.FillPath(ink, part.Path);
            }
        foreach (var part in _queue) Draw(part);
        _queue.Clear();
    }

    void Light(float amount, out float x, out float y)
    {
        double a = (225 - _rot) * Math.PI / 180;
        x = (float)Math.Cos(a) * amount; y = (float)Math.Sin(a) * amount;
    }

    void Draw(Part p)
    {
        var box = p.Path.GetBounds();
        box.Inflate(1, 1);
        using (var b = new LinearGradientBrush(box, p.C.L, p.C.M, 55f - _rot))
        {
            var blend = new ColorBlend(3);
            blend.Colors = new[] { p.C.L, p.C.M, p.C.M };
            blend.Positions = new[] { 0f, .5f, 1f };
            b.InterpolationColors = blend;
            G2.FillPath(b, p.Path);
        }

        var keep = G2.Save();
        G2.SetClip(p.Path, CombineMode.Intersect);
        if (p.Shade > 0)
        {
            float size = Math.Min(box.Width, box.Height);
            float depth = Math.Max(3f, Math.Min(15f, size * .16f)) * p.Shade;
            float rim = Math.Max(2f, Math.Min(5f, size * .05f)) * p.Shade;
            float x, y;

            Light(depth, out x, out y);
            using (var region = new Region(p.Path))
            using (var lit = (GraphicsPath)p.Path.Clone())
            using (var b = new SolidBrush(Color.FromArgb(190, p.C.D)))
            {
                G.Move(lit, x, y);
                region.Exclude(lit);
                G2.FillRegion(b, region);
            }

            Light(-rim, out x, out y);
            using (var region = new Region(p.Path))
            using (var lit = (GraphicsPath)p.Path.Clone())
            using (var b = new SolidBrush(Color.FromArgb(120, 255, 255, 255)))
            {
                G.Move(lit, x, y);
                region.Exclude(lit);
                G2.FillRegion(b, region);
            }
        }
        if (p.Decor != null) p.Decor();
        G2.Restore(keep);

        if (p.Line > 0)
            using (var pen = new Pen(Ink, p.Line) { LineJoin = LineJoin.Round })
                G2.DrawPath(pen, p.Path);
    }

    // ---------- Immediate drawing, for backgrounds and for Part.Then ----------

    public void Fill(GraphicsPath path, Color color)
    {
        using (var b = new SolidBrush(color)) G2.FillPath(b, path);
    }

    public void Fill(GraphicsPath path, Color color, int alpha) { Fill(path, Color.FromArgb(alpha, color)); }

    /// <summary>An open line through the points, for details such as cracks, stitches and rivet rows.</summary>
    public void Stroke(Color color, float width, params float[] xy)
    {
        using (var pen = new Pen(color, width) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round })
        {
            var pts = new PointF[xy.Length / 2];
            for (int i = 0; i < pts.Length; i++) pts[i] = new PointF(xy[i * 2], xy[i * 2 + 1]);
            if (pts.Length == 2) G2.DrawLine(pen, pts[0], pts[1]); else G2.DrawCurve(pen, pts, .5f);
        }
    }

    public void Ink2(float width, params float[] xy) { Stroke(Ink, width, xy); }

    /// <summary>A small soft white highlight.</summary>
    public void Gloss(float x, float y, float rx, float ry, float rot, int alpha)
    {
        var p = G.Rot(G.Ell(x, y, rx, ry), rot, x, y);
        Fill(p, Color.FromArgb(alpha, 255, 255, 255));
    }

    /// <summary>A soft round glow, strongest in the middle.</summary>
    public void Glow(float x, float y, float r, Color color, int alpha)
    {
        using (var path = G.Circle(x, y, r))
        using (var b = new PathGradientBrush(path))
        {
            b.CenterPoint = new PointF(x, y);
            b.CenterColor = Color.FromArgb(alpha, color);
            b.SurroundColors = new[] { Color.FromArgb(0, color) };
            G2.FillPath(b, path);
        }
    }

    /// <summary>A four-point twinkle with a thin ink edge.</summary>
    public void Spark(float x, float y, float r, Color color)
    {
        var p = G.Star(x, y, r, r * .26f, 4, 0);
        using (var pen = new Pen(Ink, 5f) { LineJoin = LineJoin.Round }) G2.DrawPath(pen, p);
        Fill(p, color);
    }

    /// <summary>Scales the 4x canvas down to the final 256 x 256 image.</summary>
    public Bitmap Result()
    {
        Flush();
        var result = new Bitmap(Size, Size, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(result))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.CompositingMode = CompositingMode.SourceCopy;
            g.DrawImage(_bmp, new Rectangle(0, 0, Size, Size));
        }
        return result;
    }
}
