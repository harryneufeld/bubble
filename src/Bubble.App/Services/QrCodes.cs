using QRCoder;

namespace Bubble.App.Services;

/// <summary>Erzeugt QR-Codes lokal auf dem Gerät – der Einladungslink verlässt das Gerät dafür nicht.</summary>
public static class QrCodes
{
    public static ImageSource Create(string text)
    {
        using var data = QRCodeGenerator.GenerateQrCode(text, QRCodeGenerator.ECCLevel.M);
        // Dunkle Module in der App-Grundfarbe auf Weiß: so lesen alle Scanner zuverlässig.
        var png = new PngByteQRCode(data).GetGraphic(12, [0x0B, 0x0A, 0x10, 0xFF], [0xFF, 0xFF, 0xFF, 0xFF], drawQuietZones: true);
        return ImageSource.FromStream(() => new MemoryStream(png));
    }
}
