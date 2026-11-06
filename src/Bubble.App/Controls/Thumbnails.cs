using System.Runtime.CompilerServices;
using SkiaSharp;

namespace Bubble.App.Controls;

/// <summary>
/// Small copies of photos for the week, the calendar and Rewind. Showing the original there means handing the whole
/// file to the image loader for every tiny tile. Each photo is shrunk once, in the background, and lives as long as the photo.
/// </summary>
public static class Thumbnails
{
    const int Longest = 360, JpegQuality = 80;
    static readonly ConditionalWeakTable<byte[], Task<ImageSource>> Cache = new();

    public static Task<ImageSource> ForAsync(byte[] photo) => Cache.GetValue(photo, b => Task.Run(() =>
    {
        var small = Shrink(b);
        return ImageSource.FromStream(() => new MemoryStream(small));
    }));

    static readonly ConditionalWeakTable<byte[], object> Ratios = new();

    /// <summary>Width by height, read from the file header without decoding the photo.</summary>
    public static double RatioOf(byte[] photo) => (double)Ratios.GetValue(photo, b =>
    {
        using var codec = SKCodec.Create(new MemoryStream(b));
        return codec is { Info.Height: > 0 } ? (double)codec.Info.Width / codec.Info.Height : 0.8;
    });

    static byte[] Shrink(byte[] photo)
    {
        using var codec = SKCodec.Create(new MemoryStream(photo));
        if (codec is null) return photo;
        var scale = (float)Longest / Math.Max(codec.Info.Width, codec.Info.Height);
        if (scale >= 1) return photo;

        // A JPEG can be decoded at a reduced size straight away, which saves most of the work
        var decoded = codec.GetScaledDimensions(scale);
        using var bitmap = SKBitmap.Decode(codec, new SKImageInfo(decoded.Width, decoded.Height));
        if (bitmap is null) return photo;
        var target = new SKImageInfo((int)Math.Max(1, codec.Info.Width * scale), (int)Math.Max(1, codec.Info.Height * scale));
        using var resized = bitmap.Resize(target, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None));
        using var image = SKImage.FromBitmap(resized ?? bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, JpegQuality);
        return data.ToArray();
    }
}
