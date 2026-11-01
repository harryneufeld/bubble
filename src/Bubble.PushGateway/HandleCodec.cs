using System.Security.Cryptography;
using System.Text;

namespace Bubble.PushGateway;

/// <summary>
/// A handle is the device's FCM/APNs token, sealed with a key only the gateway knows. Relays store the handle and
/// present it to wake the device; they never learn the token. The gateway keeps no state: everything it needs is in the handle.
/// </summary>
/// <remarks>Layout before sealing: platform (1 byte), expiry as Unix seconds (8 bytes), token (UTF-8).</remarks>
public sealed class HandleCodec
{
    const int NonceSize = 12;
    const int TagSize = 16;
    static readonly byte[] Aad = "bubble/push/v1"u8.ToArray();

    readonly byte[][] _keys;
    readonly TimeProvider _time;
    readonly TimeSpan _lifetime;

    /// <param name="secrets">The current secret first, then older ones that are still accepted (key rotation).</param>
    public HandleCodec(IEnumerable<string> secrets, TimeSpan lifetime, TimeProvider time)
    {
        _keys = [.. secrets.Select(s => SHA256.HashData(Encoding.UTF8.GetBytes(s)))];
        if (_keys.Length == 0) throw new ArgumentException("At least one secret is required.", nameof(secrets));
        _lifetime = lifetime;
        _time = time;
    }

    public string Seal(Platform platform, string token)
    {
        var expires = _time.GetUtcNow().Add(_lifetime).ToUnixTimeSeconds();
        var plaintext = new byte[1 + 8 + Encoding.UTF8.GetByteCount(token)];
        plaintext[0] = (byte)platform;
        BitConverter.TryWriteBytes(plaintext.AsSpan(1, 8), expires);
        Encoding.UTF8.GetBytes(token, plaintext.AsSpan(9));

        var sealedBytes = new byte[NonceSize + plaintext.Length + TagSize];
        var nonce = sealedBytes.AsSpan(0, NonceSize);
        RandomNumberGenerator.Fill(nonce);
        using var aes = new AesGcm(_keys[0], TagSize);
        aes.Encrypt(nonce, plaintext, sealedBytes.AsSpan(NonceSize, plaintext.Length), sealedBytes.AsSpan(NonceSize + plaintext.Length, TagSize), Aad);
        return Base64Url(sealedBytes);
    }

    /// <summary>Null if the handle is forged, damaged or expired.</summary>
    public (Platform Platform, string Token)? Open(string handle)
    {
        byte[] sealedBytes;
        try { sealedBytes = FromBase64Url(handle); }
        catch (FormatException) { return null; }
        if (sealedBytes.Length < NonceSize + 9 + TagSize) return null;

        var plaintext = new byte[sealedBytes.Length - NonceSize - TagSize];
        foreach (var key in _keys)
        {
            try
            {
                using var aes = new AesGcm(key, TagSize);
                aes.Decrypt(sealedBytes.AsSpan(0, NonceSize), sealedBytes.AsSpan(NonceSize, plaintext.Length),
                    sealedBytes.AsSpan(NonceSize + plaintext.Length, TagSize), plaintext, Aad);
            }
            catch (CryptographicException)
            {
                continue;
            }

            var expires = BitConverter.ToInt64(plaintext, 1);
            if (expires <= _time.GetUtcNow().ToUnixTimeSeconds() || !Enum.IsDefined((Platform)plaintext[0])) return null;
            return ((Platform)plaintext[0], Encoding.UTF8.GetString(plaintext, 9, plaintext.Length - 9));
        }
        return null;
    }

    static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    static byte[] FromBase64Url(string s)
    {
        var padded = s.Replace('-', '+').Replace('_', '/');
        padded = padded.PadRight(padded.Length + (4 - padded.Length % 4) % 4, '=');
        return Convert.FromBase64String(padded);
    }
}

public enum Platform : byte
{
    Fcm = 1,
    Apns = 2,
}
