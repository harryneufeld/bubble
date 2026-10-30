using SkiaSharp;

namespace Bubble.App.Editor;

/// <summary>Canvas format. Units are export pixels: the canvas is always 1080 wide.</summary>
public enum DesignFormat { Story, Portrait }

public enum BackgroundKind { Solid, Gradient, Photo }

/// <summary>
/// A design in the editor: background plus layers from back to front.
/// Positions are in canvas units (= pixels of the exported JPEG).
/// </summary>
public sealed class Design
{
    public const float Width = 1080;

    public DesignFormat Format { get; set; } = DesignFormat.Story;
    public BackgroundKind Background { get; set; } = BackgroundKind.Gradient;
    public SKColor BackgroundColor { get; set; } = new(0x17, 0x15, 0x1F);
    public int GradientIndex { get; set; }

    /// <summary>For <see cref="BackgroundKind.Photo"/>: sanitized JPEG, drawn blurred and darkened.</summary>
    public byte[]? BackgroundPhoto { get; set; }

    public List<Layer> Layers { get; set; } = [];

    public float Height => HeightOf(Format);
    public static float HeightOf(DesignFormat format) => format == DesignFormat.Story ? 1920 : 1350;

    /// <summary>Deep copy for undo. Image bytes are shared, they never change.</summary>
    public Design Clone() => new()
    {
        Format = Format, Background = Background, BackgroundColor = BackgroundColor,
        GradientIndex = GradientIndex, BackgroundPhoto = BackgroundPhoto,
        Layers = Layers.Select(l => l.Clone()).ToList(),
    };
}

/// <summary>Something on the canvas. X/Y is the center, the box is <see cref="BaseWidth"/> × <see cref="BaseHeight"/> times <see cref="Scale"/>.</summary>
public abstract class Layer
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public float X { get; set; }
    public float Y { get; set; }
    public float Scale { get; set; } = 1;
    /// <summary>Degrees, clockwise.</summary>
    public float Rotation { get; set; }
    public float Opacity { get; set; } = 1;

    public abstract float BaseWidth { get; }
    public abstract float BaseHeight { get; }

    /// <summary>All properties are values or immutable, so a shallow copy is a full copy.</summary>
    public Layer Clone() => (Layer)MemberwiseClone();

    /// <summary>Copy with a new ID, a bit offset – for "Duplicate".</summary>
    public Layer Duplicate()
    {
        var copy = Clone();
        copy.Id = Guid.NewGuid();
        copy.X += 40;
        copy.Y += 40;
        return copy;
    }
}

/// <summary>A photo in a frame. The photo fills the frame (cropped if needed). Without a photo it is a placeholder from a template.</summary>
public sealed class ImageLayer : Layer
{
    /// <summary>Sanitized JPEG (no metadata). Null = empty placeholder.</summary>
    public byte[]? Image { get; set; }
    public float FrameWidth { get; set; }
    public float FrameHeight { get; set; }
    public float CornerRadius { get; set; }

    public override float BaseWidth => FrameWidth;
    public override float BaseHeight => FrameHeight;
}

public enum TextStyle { Plain, Shadow, Box }

public sealed class TextLayer : Layer
{
    public string Text { get; set; } = "";
    public string Font { get; set; } = EditorFonts.Default.Key;
    public float FontSize { get; set; } = 96;
    public SKColor Color { get; set; } = SKColors.White;
    public SKTextAlign Align { get; set; } = SKTextAlign.Center;
    public TextStyle Style { get; set; }

    /// <summary>Measured by the renderer; set on every layout.</summary>
    internal SKSize Measured { get; set; }

    public override float BaseWidth => Measured.Width;
    public override float BaseHeight => Measured.Height;
}

public enum ShapeKind { Rectangle, Circle, Blob, Heart, Star, Line, Arrow }

public sealed class ShapeLayer : Layer
{
    public ShapeKind Kind { get; set; }
    public float Width { get; set; } = 500;
    public float Height { get; set; } = 500;
    public SKColor Color { get; set; } = new(0xFF, 0x4F, 0x9A);
    /// <summary>Filled with the bubble gradient instead of <see cref="Color"/>.</summary>
    public bool Gradient { get; set; }

    public bool IsStroke => Kind is ShapeKind.Line or ShapeKind.Arrow;

    public override float BaseWidth => Width;
    public override float BaseHeight => Height;
}
