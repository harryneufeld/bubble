using SkiaSharp;

namespace Bubble.App.Editor;

/// <summary>Ready layouts to start from. Photo frames are empty placeholders: tap one to fill it.</summary>
public static class Templates
{
    public sealed record Template(string Name, string Description, string Icon, Func<DesignFormat, Design> Create);

    public static readonly IReadOnlyList<Template> All =
    [
        new("Empty", "A gradient, nothing else", "✨", f => new Design { Format = f, Background = BackgroundKind.Gradient }),
        new("Quote", "Big words on a dark glow", "💬", Quote),
        new("Photo and caption", "One photo with a handwritten line", "🖼️", PhotoCaption),
        new("Two photos", "One above the other", "🪟", f => Collage(f, 2)),
        new("Three photos", "One big, two small", "🧩", f => Collage(f, 3)),
        new("Four photos", "A grid", "🔲", f => Collage(f, 4)),
        new("Headline", "Poster letters over a photo", "📣", Headline),
    ];

    static ImageLayer Frame(float x, float y, float w, float h, float radius = 36) =>
        new() { X = x, Y = y, FrameWidth = w, FrameHeight = h, CornerRadius = radius };

    static Design Quote(DesignFormat format)
    {
        var d = new Design { Format = format, Background = BackgroundKind.Gradient, GradientIndex = 4 };
        var cx = Design.Width / 2;
        d.Layers.Add(new ShapeLayer { Kind = ShapeKind.Blob, Gradient = true, Width = 900, Height = 900, X = cx, Y = d.Height / 2, Opacity = 0.35f });
        d.Layers.Add(new TextLayer { Text = "Today was\nthe best day", Font = "classic", FontSize = 130, X = cx, Y = d.Height / 2 - 40 });
        d.Layers.Add(new TextLayer { Text = "– me", Font = "hand", FontSize = 80, X = cx, Y = d.Height / 2 + 200, Color = new SKColor(0xB9, 0xA2, 0xFF) });
        return d;
    }

    static Design PhotoCaption(DesignFormat format)
    {
        var d = new Design { Format = format, Background = BackgroundKind.Gradient, GradientIndex = 5 };
        var h = d.Height * 0.62f;
        d.Layers.Add(Frame(Design.Width / 2, 80 + h / 2, 920, h, 48));
        d.Layers.Add(new TextLayer
        {
            Text = "A little moment", Font = "hand", FontSize = 120, X = Design.Width / 2, Y = 80 + h + (d.Height - 80 - h) / 2,
            Color = new SKColor(0x0B, 0x0A, 0x10),
        });
        return d;
    }

    static Design Collage(DesignFormat format, int count)
    {
        var d = new Design { Format = format, Background = BackgroundKind.Solid, BackgroundColor = new SKColor(0x0B, 0x0A, 0x10) };
        const float m = 36; // outer margin and gap
        var w = Design.Width - 2 * m;
        var h = d.Height - 2 * m;
        switch (count)
        {
            case 2:
                var half = (h - m) / 2;
                d.Layers.Add(Frame(Design.Width / 2, m + half / 2, w, half));
                d.Layers.Add(Frame(Design.Width / 2, m + half + m + half / 2, w, half));
                break;
            case 3:
                var top = (h - m) * 0.58f;
                var bottom = h - m - top;
                var col = (w - m) / 2;
                d.Layers.Add(Frame(Design.Width / 2, m + top / 2, w, top));
                d.Layers.Add(Frame(m + col / 2, m + top + m + bottom / 2, col, bottom));
                d.Layers.Add(Frame(m + col + m + col / 2, m + top + m + bottom / 2, col, bottom));
                break;
            default:
                var cw = (w - m) / 2;
                var ch = (h - m) / 2;
                for (var row = 0; row < 2; row++)
                    for (var c = 0; c < 2; c++)
                        d.Layers.Add(Frame(m + c * (cw + m) + cw / 2, m + row * (ch + m) + ch / 2, cw, ch));
                break;
        }
        return d;
    }

    static Design Headline(DesignFormat format)
    {
        var d = new Design { Format = format, Background = BackgroundKind.Gradient, GradientIndex = 2 };
        d.Layers.Add(Frame(Design.Width / 2, d.Height / 2 + 120, 1080, d.Height - 240, 0));
        d.Layers.Add(new TextLayer { Text = "WEEKEND", Font = "poster", FontSize = 300, X = Design.Width / 2, Y = 170, Style = TextStyle.Shadow });
        return d;
    }
}
