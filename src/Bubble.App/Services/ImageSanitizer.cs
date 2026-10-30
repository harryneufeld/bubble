using SkiaSharp;

namespace Bubble.App.Services;

/// <summary>
/// Prepares photos before encryption: rotate correctly, downscale and re-encode as JPEG.
/// Re-encoding drops all metadata (EXIF, GPS, camera model).
/// </summary>
public static class ImageSanitizer
{
    const int MaxEdge = 1600;
    const int JpegQuality = 80;

    public static async Task<byte[]> SanitizeAsync(Stream input)
    {
        using var buffer = new MemoryStream();
        await input.CopyToAsync(buffer);
        buffer.Position = 0;
        return await Task.Run(() => Sanitize(buffer));
    }

    /// <summary>Profilbild: mittig quadratisch zugeschnitten, 512 × 512, ohne Metadaten.</summary>
    public static async Task<byte[]> SanitizeAvatarAsync(Stream input)
    {
        using var buffer = new MemoryStream();
        await input.CopyToAsync(buffer);
        buffer.Position = 0;
        return await Task.Run(() =>
        {
            const int edge = 512;
            using var oriented = DecodeOriented(buffer);
            var side = Math.Min(oriented.Width, oriented.Height);
            using var square = new SKBitmap(edge, edge);
            using (var canvas = new SKCanvas(square))
            using (var image = SKImage.FromBitmap(oriented))
            {
                var src = SKRect.Create((oriented.Width - side) / 2f, (oriented.Height - side) / 2f, side, side);
                canvas.DrawImage(image, src, SKRect.Create(edge, edge), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
            }
            using var result = SKImage.FromBitmap(square);
            using var data = result.Encode(SKEncodedImageFormat.Jpeg, 85);
            return data.ToArray();
        });
    }

    static SKBitmap DecodeOriented(Stream input)
    {
        using var codec = SKCodec.Create(input) ?? throw new InvalidDataException("Image format is not supported.");
        using var decoded = SKBitmap.Decode(codec);
        return ApplyOrigin(decoded, codec.EncodedOrigin);
    }

    static byte[] Sanitize(Stream input)
    {
        using var oriented = DecodeOriented(input);

        var scale = Math.Min(1f, (float)MaxEdge / Math.Max(oriented.Width, oriented.Height));
        var info = new SKImageInfo((int)(oriented.Width * scale), (int)(oriented.Height * scale));
        using var resized = scale < 1f
            ? oriented.Resize(info, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear))
            : oriented.Copy();

        using var image = SKImage.FromBitmap(resized);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, JpegQuality);
        return data.ToArray();
    }

    static SKBitmap ApplyOrigin(SKBitmap source, SKEncodedOrigin origin)
    {
        var (degrees, swap) = origin switch
        {
            SKEncodedOrigin.RightTop => (90, true),
            SKEncodedOrigin.BottomRight => (180, false),
            SKEncodedOrigin.LeftBottom => (270, true),
            _ => (0, false),
        };
        if (degrees == 0) return source.Copy();

        var result = new SKBitmap(swap ? source.Height : source.Width, swap ? source.Width : source.Height);
        using var canvas = new SKCanvas(result);
        canvas.Translate(result.Width / 2f, result.Height / 2f);
        canvas.RotateDegrees(degrees);
        canvas.Translate(-source.Width / 2f, -source.Height / 2f);
        canvas.DrawBitmap(source, 0, 0);
        return result;
    }
}
