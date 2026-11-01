using SkiaSharp;

namespace Bubble.App.Editor;

/// <summary>
/// Flat one-color icons for the editor. Each one is a path in a 100 × 100 square around the origin,
/// so it scales, rotates and takes a color or the bubble gradient like any other shape.
/// Overlapping parts add up (non-zero fill); holes are drawn the other way round.
/// </summary>
public static class IconShapes
{
    /// <summary>The icons in the order of the picker.</summary>
    public static readonly (ShapeKind Kind, string Name)[] All =
    [
        (ShapeKind.Sun, "Sun"), (ShapeKind.Moon, "Moon"), (ShapeKind.Cloud, "Cloud"), (ShapeKind.Bolt, "Lightning"),
        (ShapeKind.Flower, "Flower"), (ShapeKind.Tree, "Tree"), (ShapeKind.Flame, "Flame"), (ShapeKind.Sparkle, "Sparkle"),
        (ShapeKind.Smiley, "Smiley"), (ShapeKind.Speech, "Speech"), (ShapeKind.Crown, "Crown"), (ShapeKind.Note, "Music"),
        (ShapeKind.Camera, "Camera"), (ShapeKind.Pin, "Place"), (ShapeKind.Home, "Home"), (ShapeKind.Coffee, "Coffee"),
    ];

    public static bool IsIcon(ShapeKind kind) => All.Any(i => i.Kind == kind);

    /// <summary>The icon scaled into <paramref name="box"/>.</summary>
    public static SKPath PathIn(ShapeKind kind, SKRect box)
    {
        var path = Build(kind);
        path.Transform(SKMatrix.CreateScaleTranslation(box.Width / 100, box.Height / 100, box.MidX, box.MidY));
        return path;
    }

    /// <summary>A square PNG of the icon in one color, for the picker.</summary>
    public static byte[] Preview(ShapeKind kind, int size, SKColor color)
    {
        using var bitmap = new SKBitmap(size, size);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);
        using var path = PathIn(kind, SKRect.Create(size * 0.1f, size * 0.1f, size * 0.8f, size * 0.8f));
        using var paint = new SKPaint { IsAntialias = true, Color = color };
        canvas.DrawPath(path, paint);
        using var image = SKImage.FromBitmap(bitmap);
        return image.Encode(SKEncodedImageFormat.Png, 100).ToArray();
    }

    static SKPath Build(ShapeKind kind)
    {
        var p = new SKPath();
        switch (kind)
        {
            case ShapeKind.Sun:
                Circle(p, 0, 0, 20);
                for (var i = 0; i < 8; i++)
                {
                    var a = i * Math.PI / 4;
                    Ray(p, a, 31, 46, 4.5f);
                }
                break;

            case ShapeKind.Moon:
            {
                using var disc = new SKPath();
                disc.AddCircle(-2, 0, 40);
                using var bite = new SKPath();
                bite.AddCircle(18, -10, 34);
                using var crescent = disc.Op(bite, SKPathOp.Difference);
                if (crescent is not null) p.AddPath(crescent);
                break;
            }

            case ShapeKind.Cloud:
                Circle(p, -20, 6, 17);
                Circle(p, 0, -8, 23);
                Circle(p, 24, 8, 16);
                Round(p, -37, 6, 74, 26, 13);
                break;

            case ShapeKind.Bolt:
                Poly(p, false, 10, -46, -24, 4, -3, 4, -12, 46, 24, -10, 3, -10);
                break;

            case ShapeKind.Flower:
                for (var i = 0; i < 5; i++)
                {
                    var a = -Math.PI / 2 + i * 2 * Math.PI / 5;
                    Circle(p, (float)Math.Cos(a) * 25, (float)Math.Sin(a) * 25, 18);
                }
                Circle(p, 0, 0, 17);
                Circle(p, 0, 0, 7, hole: true);
                break;

            case ShapeKind.Tree:
                Poly(p, false, 0, -46, -26, -10, 26, -10);
                Poly(p, false, 0, -28, -34, 16, 34, 16);
                Round(p, -6, 12, 12, 34, 2);
                break;

            case ShapeKind.Flame:
                p.MoveTo(0, -46);
                p.CubicTo(10, -24, 36, -14, 36, 14);
                p.CubicTo(36, 34, 20, 46, 0, 46);
                p.CubicTo(-20, 46, -36, 34, -36, 14);
                p.CubicTo(-36, -2, -26, -8, -22, -22);
                p.CubicTo(-14, -12, -8, -20, 0, -46);
                p.Close();
                Circle(p, 0, 24, 11, hole: true);
                break;

            case ShapeKind.Sparkle:
                p.MoveTo(0, -46);
                p.QuadTo(5, -5, 46, 0);
                p.QuadTo(5, 5, 0, 46);
                p.QuadTo(-5, 5, -46, 0);
                p.QuadTo(-5, -5, 0, -46);
                p.Close();
                break;

            case ShapeKind.Smiley:
                Circle(p, 0, 0, 44);
                Circle(p, -16, -12, 6, hole: true);
                Circle(p, 16, -12, 6, hole: true);
                Smile(p);
                break;

            case ShapeKind.Speech:
                Round(p, -44, -36, 88, 58, 16);
                Poly(p, false, -26, 14, -30, 44, 6, 14);
                Circle(p, -19, -7, 5, hole: true);
                Circle(p, 0, -7, 5, hole: true);
                Circle(p, 19, -7, 5, hole: true);
                break;

            case ShapeKind.Crown:
                Poly(p, false, -42, 30, -46, -22, -22, 2, 0, -32, 22, 2, 46, -22, 42, 30);
                Circle(p, -46, -27, 6);
                Circle(p, 0, -37, 6);
                Circle(p, 46, -27, 6);
                break;

            case ShapeKind.Note:
                Circle(p, -17, 30, 14);
                Round(p, -5, -40, 9, 70, 2);
                Poly(p, false, 4, -40, 32, -24, 30, -4, 4, -20);
                break;

            case ShapeKind.Camera:
                Round(p, -44, -24, 88, 64, 10);
                Round(p, -18, -38, 36, 20, 6);
                Circle(p, 0, 10, 21, hole: true);
                Circle(p, 0, 10, 12);
                break;

            case ShapeKind.Pin:
                Circle(p, 0, -14, 30);
                Poly(p, false, -26, -2, 26, -2, 0, 46);
                Circle(p, 0, -14, 11, hole: true);
                break;

            case ShapeKind.Home:
                Poly(p, false, 0, -44, 46, -2, 36, -2, 36, 40, -36, 40, -36, -2, -46, -2);
                Round(p, -9, 12, 18, 28, 3, hole: true);
                break;

            case ShapeKind.Coffee:
                Round(p, -38, -14, 64, 54, 12);
                Round(p, 18, -4, 28, 30, 12);
                Round(p, 28, 4, 10, 14, 4, hole: true);
                Round(p, -22, -40, 7, 18, 3);
                Round(p, -4, -44, 7, 22, 3);
                break;
        }
        return p;
    }

    static void Circle(SKPath p, float x, float y, float r, bool hole = false) =>
        p.AddCircle(x, y, r, hole ? SKPathDirection.CounterClockwise : SKPathDirection.Clockwise);

    static void Round(SKPath p, float x, float y, float w, float h, float r, bool hole = false) =>
        p.AddRoundRect(SKRect.Create(x, y, w, h), r, r, hole ? SKPathDirection.CounterClockwise : SKPathDirection.Clockwise);

    /// <summary>Corner points as x, y pairs. The direction is fixed afterwards: filled clockwise, holes counter-clockwise.</summary>
    static void Poly(SKPath p, bool hole, params float[] xy) =>
        Poly(p, hole, Enumerable.Range(0, xy.Length / 2).Select(i => new SKPoint(xy[2 * i], xy[2 * i + 1])).ToArray());

    static void Poly(SKPath p, bool hole, params SKPoint[] points)
    {
        double area = 0;
        for (var i = 0; i < points.Length; i++)
        {
            var a = points[i];
            var b = points[(i + 1) % points.Length];
            area += a.X * b.Y - b.X * a.Y;
        }
        if ((area > 0) == hole) points = points.Reverse().ToArray(); // positive area = clockwise on screen
        p.AddPoly(points, close: true);
    }

    /// <summary>A short bar pointing away from the center, at <paramref name="angle"/>, between two radii.</summary>
    static void Ray(SKPath p, double angle, float from, float to, float halfWidth)
    {
        var dir = new SKPoint((float)Math.Cos(angle), (float)Math.Sin(angle));
        var side = new SKPoint(-dir.Y * halfWidth, dir.X * halfWidth);
        SKPoint At(float r, float s) => new(dir.X * r + side.X * s, dir.Y * r + side.Y * s);
        Poly(p, false, At(from, -1), At(to, -1), At(to, 1), At(from, 1));
    }

    /// <summary>A curved smile cut out of the face: the lower part of a ring.</summary>
    static void Smile(SKPath p)
    {
        const int steps = 12;
        var points = new List<SKPoint>();
        for (var i = 0; i <= steps; i++)
        {
            var a = (25 + 130.0 * i / steps) * Math.PI / 180;
            points.Add(new SKPoint((float)Math.Cos(a) * 27, 2 + (float)Math.Sin(a) * 27));
        }
        for (var i = steps; i >= 0; i--)
        {
            var a = (25 + 130.0 * i / steps) * Math.PI / 180;
            points.Add(new SKPoint((float)Math.Cos(a) * 19, 2 + (float)Math.Sin(a) * 19));
        }
        Poly(p, true, points.ToArray());
    }
}
