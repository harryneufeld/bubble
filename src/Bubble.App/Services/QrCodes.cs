using QRCoder;

namespace Bubble.App.Services;

/// <summary>Generates QR codes locally on the device – the invitation link does not leave the device for this.</summary>
public static class QrCodes
{
    public static ImageSource Create(string text)
    {
        using var data = QRCodeGenerator.GenerateQrCode(text, QRCodeGenerator.ECCLevel.M);
        // Dark modules in the app base color on white: all scanners read this reliably.
        var png = new PngByteQRCode(data).GetGraphic(12, [0x0B, 0x0A, 0x10, 0xFF], [0xFF, 0xFF, 0xFF, 0xFF], drawQuietZones: true);
        return ImageSource.FromStream(() => new MemoryStream(png));
    }
}
