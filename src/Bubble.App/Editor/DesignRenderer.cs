using System.Runtime.CompilerServices;
using System.Text;
using SkiaSharp;

namespace Bubble.App.Editor;

/// <summary>
/// Draws a design with SkiaSharp – the same code for the editor view and the exported JPEG,
/// so what you see is exactly what gets shared.
/// </summary>
public static class DesignRenderer
{
    const int JpegQuality = 88;

    public static readonly SKColor[] Swatches =
    [
        SKColors.White, new(0x0B, 0x0A, 0x10), new(0xFF, 0x4F, 0x9A), new(0x8B, 0x5C, 0xF6), new(0x22, 0xD3, 0xEE),
        new(0xB9, 0xA2, 0xFF), new(0xFF, 0xD1, 0x66), new(0xFF, 0x8A, 0x3D), new(0x4A, 0xDE, 0x80), new(0xFF, 0x5A, 0x6E),
    ];

    /// <summary>Background gradients from pink, violet, cyan and their neighbors (DESIGN.md §10).</summary>
    public static readonly SKColor[][] Gradients =
    [
        [new(0xFF, 0x4F, 0x9A), new(0x8B, 0x5C, 0xF6), new(0x22, 0xD3, 0xEE)],
        [new(0x8B, 0x5C, 0xF6), new(0x2A, 0x1B, 0x5C)],
        [new(0xFF, 0x8A, 0x3D), new(0xFF, 0x4F, 0x9A)],
        [new(0x22, 0xD3, 0xEE), new(0x6D, 0x3C, 0xF5)],
        [new(0x0B, 0x0A, 0x10), new(0x3B, 0x1E, 0x6E)],
        [new(0xFF, 0xD1, 0x66), new(0xFF, 0xB4, 0xA2), new(0xFF, 0x4F, 0x9A)],
    ];

    static readonly SKSamplingOptions Sampling = new(SKFilterMode.Linear, SKMipmapMode.Linear);
    static readonly ConditionalWeakTable<byte[], SKImage> Images = new();
    static readonly ConditionalWeakTable<byte[], SKImage> Blurred = new();

    /// <summary>Renders the design to a JPEG at full size (1080 wide). Placeholders and selection are left out.</summary>
    public static byte[] Export(Design design)
    {
        var info = new SKImageInfo((int)Design.Width, (int)design.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);
        Draw(surface.Canvas, design, forExport: true);
        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, JpegQuality);
        return data.ToArray();
    }

    /// <summary>Draws in canvas units; the caller scales the canvas to the view.</summary>
    public static void Draw(SKCanvas canvas, Design design, bool forExport = false)
    {
        var bounds = SKRect.Create(Design.Width, design.Height);
        canvas.Save();
        canvas.ClipRect(bounds);
        DrawBackground(canvas, design, bounds);
        foreach (var layer in design.Layers)
        {
            if (forExport && layer is ImageLayer { Image: null }) continue;
            canvas.Save();
            ApplyTransform(canvas, layer);
            using var fade = layer.Opacity < 1 ? new SKPaint { Color = SKColors.White.WithAlpha((byte)(layer.Opacity * 255)) } : null;
            if (fade is not null) canvas.SaveLayer(fade);
            switch (layer)
            {
                case ImageLayer image: DrawImage(canvas, image); break;
                case TextLayer text: DrawText(canvas, text); break;
                case ShapeLayer shape: DrawShape(canvas, shape); break;
            }
            if (fade is not null) canvas.Restore();
            canvas.Restore();
        }
        canvas.Restore();
    }

    static void ApplyTransform(SKCanvas canvas, Layer layer)
    {
        canvas.Translate(layer.X, layer.Y);
        canvas.RotateDegrees(layer.Rotation);
        canvas.Scale(layer.Scale);
    }

    // ---------- Background ----------

    static void DrawBackground(SKCanvas canvas, Design design, SKRect bounds)
    {
        switch (design.Background)
        {
            case BackgroundKind.Solid:
                canvas.DrawColor(design.BackgroundColor);
                break;
            case BackgroundKind.Gradient:
                using (var paint = new SKPaint { Shader = GradientShader(design.GradientIndex, bounds) })
                    canvas.DrawRect(bounds, paint);
                break;
            case BackgroundKind.Photo when design.BackgroundPhoto is { } bytes:
                var blurred = Blurred.GetValue(bytes, MakeBlurred);
                canvas.DrawImage(blurred, CoverSource(blurred.Width, blurred.Height, bounds.Width, bounds.Height), bounds, Sampling);
                canvas.DrawColor(new SKColor(0, 0, 0, 90));
                break;
            default:
                canvas.DrawColor(design.BackgroundColor);
                break;
        }
    }

    public static SKShader GradientShader(int index, SKRect bounds)
    {
        var colors = Gradients[Math.Clamp(index, 0, Gradients.Length - 1)];
        return SKShader.CreateLinearGradient(new SKPoint(bounds.Left, bounds.Top), new SKPoint(bounds.Right, bounds.Bottom),
            colors, null, SKShaderTileMode.Clamp);
    }

    /// <summary>Blurs a small copy once; scaled up it looks like a strong blur and costs nothing per frame.</summary>
    static SKImage MakeBlurred(byte[] bytes)
    {
        var source = Image(bytes);
        var scale = 240f / Math.Max(source.Width, source.Height);
        var info = new SKImageInfo(Math.Max(1, (int)(source.Width * scale)), Math.Max(1, (int)(source.Height * scale)));
        using var surface = SKSurface.Create(info);
        using var paint = new SKPaint { ImageFilter = SKImageFilter.CreateBlur(6, 6, SKShaderTileMode.Clamp) };
        surface.Canvas.DrawImage(source, SKRect.Create(info.Width, info.Height), Sampling, paint);
        return surface.Snapshot();
    }

    static SKImage Image(byte[] bytes) => Images.GetValue(bytes, b => SKImage.FromEncodedData(b) ?? throw new InvalidDataException());

    /// <summary>Source rectangle so the image covers the target without distortion (center crop).</summary>
    static SKRect CoverSource(float imageWidth, float imageHeight, float targetWidth, float targetHeight)
    {
        var scale = Math.Max(targetWidth / imageWidth, targetHeight / imageHeight);
        var w = targetWidth / scale;
        var h = targetHeight / scale;
        return SKRect.Create((imageWidth - w) / 2, (imageHeight - h) / 2, w, h);
    }

    // ---------- Photo ----------

    static void DrawImage(SKCanvas canvas, ImageLayer layer)
    {
        var frame = SKRect.Create(-layer.FrameWidth / 2, -layer.FrameHeight / 2, layer.FrameWidth, layer.FrameHeight);
        canvas.Save();
        if (layer.CornerRadius > 0) canvas.ClipRoundRect(new SKRoundRect(frame, layer.CornerRadius), antialias: true);
        else canvas.ClipRect(frame);

        if (layer.Image is { } bytes)
        {
            var image = Image(bytes);
            canvas.DrawImage(image, CropSource(layer, image), frame, Sampling);
        }
        else
        {
            // Placeholder from a template: only visible in the editor
            using var fill = new SKPaint { Color = new SKColor(0x22, 0x1F, 0x2C, 220) };
            canvas.DrawRect(frame, fill);
            var arm = Math.Min(frame.Width, frame.Height) * 0.08f;
            using var plus = new SKPaint { Color = new SKColor(0xC9, 0xC4, 0xD6), StrokeWidth = arm * 0.22f, IsAntialias = true, StrokeCap = SKStrokeCap.Round };
            canvas.DrawLine(-arm, 0, arm, 0, plus);
            canvas.DrawLine(0, -arm, 0, arm, plus);
        }
        canvas.Restore();
    }

    // ---------- Crop ----------

    public static SKSizeI ImageSize(byte[] bytes)
    {
        var image = Image(bytes);
        return new SKSizeI(image.Width, image.Height);
    }

    /// <summary>Frame units per photo pixel: just covering the frame, times the crop zoom.</summary>
    public static float CropScale(ImageLayer layer, SKSizeI image) =>
        Math.Max(layer.FrameWidth / image.Width, layer.FrameHeight / image.Height) * layer.CropZoom;

    /// <summary>Keeps the crop inside the photo, so the frame is always filled.</summary>
    public static void ClampCrop(ImageLayer layer)
    {
        if (layer.Image is not { } bytes) return;
        var image = ImageSize(bytes);
        layer.CropZoom = Math.Clamp(layer.CropZoom, 1, 8);
        var scale = CropScale(layer, image);
        var halfW = Math.Min(0.5f, layer.FrameWidth / scale / image.Width / 2);
        var halfH = Math.Min(0.5f, layer.FrameHeight / scale / image.Height / 2);
        layer.CropX = Math.Clamp(layer.CropX, halfW, 1 - halfW);
        layer.CropY = Math.Clamp(layer.CropY, halfH, 1 - halfH);
    }

    static SKRect CropSource(ImageLayer layer, SKImage image)
    {
        var scale = CropScale(layer, new SKSizeI(image.Width, image.Height));
        var w = layer.FrameWidth / scale;
        var h = layer.FrameHeight / scale;
        return SKRect.Create(layer.CropX * image.Width - w / 2, layer.CropY * image.Height - h / 2, w, h);
    }

    /// <summary>
    /// Crop mode: the cut-away part of the photo shows dimmed around the frame,
    /// with thirds lines and grips on the edges for resizing the frame.
    /// </summary>
    public static void DrawCropOverlay(SKCanvas canvas, ImageLayer layer, float unit)
    {
        if (layer.Image is not { } bytes) return;
        var image = Image(bytes);
        var scale = CropScale(layer, new SKSizeI(image.Width, image.Height));
        var frame = SKRect.Create(-layer.FrameWidth / 2, -layer.FrameHeight / 2, layer.FrameWidth, layer.FrameHeight);
        var whole = SKRect.Create(-layer.CropX * image.Width * scale, -layer.CropY * image.Height * scale, image.Width * scale, image.Height * scale);

        canvas.Save();
        ApplyTransform(canvas, layer);
        var u = unit / layer.Scale; // one screen pixel in layer units

        canvas.Save();
        canvas.ClipRect(frame, SKClipOperation.Difference);
        using (var ghost = new SKPaint { Color = SKColors.White.WithAlpha(90) })
            canvas.DrawImage(image, whole, Sampling, ghost);
        canvas.Restore();

        using var line = new SKPaint { Style = SKPaintStyle.Stroke, StrokeWidth = 1 * u, Color = SKColors.White.WithAlpha(140), IsAntialias = true };
        for (var i = 1; i < 3; i++)
        {
            canvas.DrawLine(frame.Left + frame.Width * i / 3, frame.Top, frame.Left + frame.Width * i / 3, frame.Bottom, line);
            canvas.DrawLine(frame.Left, frame.Top + frame.Height * i / 3, frame.Right, frame.Top + frame.Height * i / 3, line);
        }
        line.StrokeWidth = 2 * u;
        line.Color = SKColors.White;
        canvas.DrawRect(frame, line);

        using var grip = new SKPaint { Color = SKColors.White, IsAntialias = true };
        float len = 36 * u, thick = 6 * u;
        canvas.DrawRoundRect(SKRect.Create(-len / 2, frame.Top - thick / 2, len, thick), thick / 2, thick / 2, grip);
        canvas.DrawRoundRect(SKRect.Create(-len / 2, frame.Bottom - thick / 2, len, thick), thick / 2, thick / 2, grip);
        canvas.DrawRoundRect(SKRect.Create(frame.Left - thick / 2, -len / 2, thick, len), thick / 2, thick / 2, grip);
        canvas.DrawRoundRect(SKRect.Create(frame.Right - thick / 2, -len / 2, thick, len), thick / 2, thick / 2, grip);
        canvas.Restore();
    }

    // ---------- Text ----------

    sealed record Run(string Text, SKFont Font, float Width);
    sealed record Line(List<Run> Runs, float Width);

    /// <summary>Lays out the text and stores its size in <see cref="TextLayer.Measured"/>.</summary>
    public static void Measure(TextLayer layer) => Layout(layer, out _);

    static float LineHeight(SKFont font) => (font.Metrics.Descent - font.Metrics.Ascent) * 1.08f;
    static float Padding(TextLayer layer) => layer.Style == TextStyle.Box ? layer.FontSize * 0.4f : 0;

    static List<Line> Layout(TextLayer layer, out SKFont main)
    {
        main = new SKFont(EditorFonts.Typeface(layer.Font), layer.FontSize) { Subpixel = true, Edging = SKFontEdging.Antialias };
        var lines = new List<Line>();
        var text = string.IsNullOrEmpty(layer.Text) ? " " : layer.Text;
        foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
        {
            var runs = SplitRuns(raw, main);
            lines.Add(new Line(runs, runs.Sum(r => r.Width)));
        }
        var pad = Padding(layer);
        layer.Measured = new SKSize(Math.Max(1, lines.Max(l => l.Width)) + pad * 2, LineHeight(main) * lines.Count + pad * 2);
        return lines;
    }

    /// <summary>
    /// Splits a line into runs of one typeface each. Characters the chosen font lacks (emoji, other scripts)
    /// fall back to a system font; joiners and modifiers stay with the character before them.
    /// </summary>
    static List<Run> SplitRuns(string line, SKFont main)
    {
        var runs = new List<Run>();
        var current = new StringBuilder();
        SKTypeface? currentFace = null;
        var joinNext = false;

        foreach (var rune in line.EnumerateRunes())
        {
            var value = rune.Value;
            var sticky = joinNext || value is 0x200D or 0xFE0F or 0xFE0E or 0x20E3 or (>= 0x1F3FB and <= 0x1F3FF) or (>= 0xE0020 and <= 0xE007F);
            joinNext = value == 0x200D;
            SKTypeface face;
            if (sticky && currentFace is not null) face = currentFace;
            else if (main.Typeface.ContainsGlyph(value)) face = main.Typeface;
            else face = SKFontManager.Default.MatchCharacter(value) ?? main.Typeface;

            if (currentFace is not null && face != currentFace) Flush();
            currentFace = face;
            current.Append(rune.ToString());
        }
        Flush();
        return runs;

        void Flush()
        {
            if (current.Length == 0 || currentFace is null) return;
            var font = currentFace == main.Typeface ? main : new SKFont(currentFace, main.Size) { Subpixel = true, Edging = SKFontEdging.Antialias };
            var text = current.ToString();
            runs.Add(new Run(text, font, font.MeasureText(text)));
            current.Clear();
        }
    }

    static void DrawText(SKCanvas canvas, TextLayer layer)
    {
        var lines = Layout(layer, out var main);
        var size = layer.Measured;
        var pad = Padding(layer);
        var box = SKRect.Create(-size.Width / 2, -size.Height / 2, size.Width, size.Height);

        var textColor = layer.Color;
        if (layer.Style == TextStyle.Box)
        {
            using var fill = new SKPaint { Color = layer.Color, IsAntialias = true };
            canvas.DrawRoundRect(box, layer.FontSize * 0.35f, layer.FontSize * 0.35f, fill);
            textColor = Contrast(layer.Color);
        }

        using var paint = new SKPaint { Color = textColor, IsAntialias = true };
        if (layer.Style == TextStyle.Shadow)
            paint.ImageFilter = SKImageFilter.CreateDropShadow(0, layer.FontSize * 0.04f, layer.FontSize * 0.08f, layer.FontSize * 0.08f, new SKColor(0, 0, 0, 150));

        var lineHeight = LineHeight(main);
        var inner = size.Width - pad * 2;
        var y = box.Top + pad - main.Metrics.Ascent + (lineHeight - (main.Metrics.Descent - main.Metrics.Ascent)) / 2;
        foreach (var line in lines)
        {
            var x = box.Left + pad + layer.Align switch
            {
                SKTextAlign.Left => 0,
                SKTextAlign.Right => inner - line.Width,
                _ => (inner - line.Width) / 2,
            };
            foreach (var run in line.Runs)
            {
                canvas.DrawText(run.Text, x, y, run.Font, paint);
                x += run.Width;
            }
            y += lineHeight;
        }
    }

    /// <summary>Black or white, whichever reads better on the color.</summary>
    public static SKColor Contrast(SKColor color) =>
        0.299 * color.Red + 0.587 * color.Green + 0.114 * color.Blue > 160 ? new SKColor(0x0B, 0x0A, 0x10) : SKColors.White;

    // ---------- Shapes ----------

    static void DrawShape(SKCanvas canvas, ShapeLayer layer)
    {
        var w = layer.Width;
        var h = layer.Height;
        var box = SKRect.Create(-w / 2, -h / 2, w, h);
        using var paint = new SKPaint { IsAntialias = true, Color = layer.Color };
        if (layer.Gradient) paint.Shader = GradientShader(0, box);

        if (layer.IsStroke)
        {
            paint.Style = SKPaintStyle.Stroke;
            paint.StrokeWidth = 22;
            paint.StrokeCap = SKStrokeCap.Round;
            paint.StrokeJoin = SKStrokeJoin.Round;
            var end = w / 2 - paint.StrokeWidth / 2;
            canvas.DrawLine(-end, 0, end, 0, paint);
            if (layer.Kind == ShapeKind.Arrow)
            {
                using var head = new SKPath();
                head.MoveTo(end - 70, -60);
                head.LineTo(end, 0);
                head.LineTo(end - 70, 60);
                canvas.DrawPath(head, paint);
            }
            return;
        }

        using var path = ShapePath(layer.Kind, box);
        canvas.DrawPath(path, paint);
    }

    static SKPath ShapePath(ShapeKind kind, SKRect box)
    {
        var path = new SKPath();
        float cx = box.MidX, cy = box.MidY, rx = box.Width / 2, ry = box.Height / 2;
        switch (kind)
        {
            case ShapeKind.Rectangle:
                path.AddRoundRect(box, Math.Min(rx, ry) * 0.16f, Math.Min(rx, ry) * 0.16f);
                break;
            case ShapeKind.Circle:
                path.AddOval(box);
                break;
            case ShapeKind.Star:
                for (var i = 0; i < 10; i++)
                {
                    var angle = -Math.PI / 2 + i * Math.PI / 5;
                    var r = i % 2 == 0 ? 1f : 0.45f;
                    var p = new SKPoint(cx + (float)Math.Cos(angle) * rx * r, cy + (float)Math.Sin(angle) * ry * r * 1.05f + ry * 0.05f);
                    if (i == 0) path.MoveTo(p); else path.LineTo(p);
                }
                path.Close();
                break;
            case ShapeKind.Heart:
                path.MoveTo(cx, box.Top + box.Height * 0.3f);
                path.CubicTo(cx, box.Top + box.Height * 0.05f, box.Left, box.Top, box.Left, box.Top + box.Height * 0.32f);
                path.CubicTo(box.Left, box.Top + box.Height * 0.6f, cx - rx * 0.2f, box.Top + box.Height * 0.78f, cx, box.Bottom);
                path.CubicTo(cx + rx * 0.2f, box.Top + box.Height * 0.78f, box.Right, box.Top + box.Height * 0.6f, box.Right, box.Top + box.Height * 0.32f);
                path.CubicTo(box.Right, box.Top, cx, box.Top + box.Height * 0.05f, cx, box.Top + box.Height * 0.3f);
                path.Close();
                break;
            case ShapeKind.Blob:
                // An organic bubble: a closed Catmull-Rom curve through points at varying radii
                float[] radii = [1f, 0.95f, 0.91f, 0.94f, 0.99f, 0.93f, 0.89f, 0.92f, 0.97f, 0.9f, 0.93f, 0.98f];
                var points = radii.Select((r, i) =>
                {
                    var angle = i * Math.PI * 2 / radii.Length;
                    return new SKPoint(cx + (float)Math.Cos(angle) * rx * r, cy + (float)Math.Sin(angle) * ry * r);
                }).ToArray();
                path.MoveTo(points[0]);
                for (var i = 0; i < points.Length; i++)
                {
                    var p0 = points[(i - 1 + points.Length) % points.Length];
                    var p1 = points[i];
                    var p2 = points[(i + 1) % points.Length];
                    var p3 = points[(i + 2) % points.Length];
                    path.CubicTo(p1.X + (p2.X - p0.X) / 6, p1.Y + (p2.Y - p0.Y) / 6, p2.X - (p3.X - p1.X) / 6, p2.Y - (p3.Y - p1.Y) / 6, p2.X, p2.Y);
                }
                path.Close();
                break;
        }
        return path;
    }

    // ---------- Editing aids ----------

    /// <summary>The top-most layer under the point (canvas units), or null.</summary>
    public static Layer? HitTest(Design design, SKPoint point, float slop)
    {
        for (var i = design.Layers.Count - 1; i >= 0; i--)
        {
            var layer = design.Layers[i];
            if (layer is TextLayer text) Measure(text);
            var local = ToLocal(layer, point);
            var halfW = Math.Max(layer.BaseWidth / 2, slop / layer.Scale);
            var halfH = Math.Max(layer.BaseHeight / 2, slop / layer.Scale);
            if (Math.Abs(local.X) <= halfW && Math.Abs(local.Y) <= halfH) return layer;
        }
        return null;
    }

    /// <summary>Point in the layer's own coordinates (center = 0, before its scale).</summary>
    public static SKPoint ToLocal(Layer layer, SKPoint point)
    {
        var dx = point.X - layer.X;
        var dy = point.Y - layer.Y;
        var rad = -layer.Rotation * Math.PI / 180;
        var (sin, cos) = ((float)Math.Sin(rad), (float)Math.Cos(rad));
        return new SKPoint((dx * cos - dy * sin) / layer.Scale, (dx * sin + dy * cos) / layer.Scale);
    }

    /// <summary>Frame around the selected layer; <paramref name="unit"/> = one screen pixel in canvas units.</summary>
    public static void DrawSelection(SKCanvas canvas, Layer layer, float unit)
    {
        if (layer is TextLayer text) Measure(text);
        canvas.Save();
        canvas.Translate(layer.X, layer.Y);
        canvas.RotateDegrees(layer.Rotation);
        var w = layer.BaseWidth * layer.Scale;
        var h = Math.Max(layer.BaseHeight * layer.Scale, 24 * unit);
        var rect = SKRect.Create(-w / 2, -h / 2, w, h);
        rect.Inflate(8 * unit, 8 * unit);
        using var shadow = new SKPaint { Style = SKPaintStyle.Stroke, StrokeWidth = 4 * unit, Color = new SKColor(0, 0, 0, 90), IsAntialias = true };
        using var line = new SKPaint { Style = SKPaintStyle.Stroke, StrokeWidth = 2 * unit, Color = SKColors.White, IsAntialias = true };
        canvas.DrawRoundRect(rect, 6 * unit, 6 * unit, shadow);
        canvas.DrawRoundRect(rect, 6 * unit, 6 * unit, line);
        canvas.Restore();
    }

    /// <summary>Center guides while a layer snaps to the middle of the canvas.</summary>
    public static void DrawGuides(SKCanvas canvas, Design design, bool vertical, bool horizontal, float unit)
    {
        using var paint = new SKPaint { Color = new SKColor(0x22, 0xD3, 0xEE), StrokeWidth = 1.5f * unit, IsAntialias = true };
        if (vertical) canvas.DrawLine(Design.Width / 2, 0, Design.Width / 2, design.Height, paint);
        if (horizontal) canvas.DrawLine(0, design.Height / 2, Design.Width, design.Height / 2, paint);
    }
}
