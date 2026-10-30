using System.Security.Cryptography;
using System.Text;
using BcChaCha20Poly1305 = Org.BouncyCastle.Crypto.Modes.ChaCha20Poly1305;
using Org.BouncyCastle.Crypto.Parameters;

namespace Bubble.Core;

/// <summary>
/// Symmetric crypto building blocks. Everything runs through BouncyCastle (pure managed),
/// so it behaves identically on Android, iOS and Windows.
/// </summary>
public static class Crypto
{
    public const int KeySize = 32;
    const int NonceSize = 12;
    const int TagBits = 128;

    public static byte[] RandomBytes(int length) => RandomNumberGenerator.GetBytes(length);

    public static byte[] NewKey() => RandomBytes(KeySize);

    /// <summary>ChaCha20-Poly1305. Ausgabe: nonce || ciphertext || tag.</summary>
    public static byte[] Seal(byte[] key, byte[] plaintext, string associatedData)
    {
        var nonce = RandomBytes(NonceSize);
        var cipher = new BcChaCha20Poly1305();
        cipher.Init(true, new AeadParameters(new KeyParameter(key), TagBits, nonce, Encoding.UTF8.GetBytes(associatedData)));
        var output = new byte[NonceSize + cipher.GetOutputSize(plaintext.Length)];
        nonce.CopyTo(output, 0);
        var len = cipher.ProcessBytes(plaintext, 0, plaintext.Length, output, NonceSize);
        cipher.DoFinal(output, NonceSize + len);
        return output;
    }

    /// <summary>Decrypts <see cref="Seal"/> output. Throws <see cref="CryptographicException"/> on tampering or a wrong key.</summary>
    public static byte[] Open(byte[] key, byte[] sealedData, string associatedData)
    {
        if (sealedData.Length < NonceSize + TagBits / 8)
            throw new CryptographicException("Ciphertext too short.");
        var nonce = sealedData.AsSpan(0, NonceSize).ToArray();
        var cipher = new BcChaCha20Poly1305();
        cipher.Init(false, new AeadParameters(new KeyParameter(key), TagBits, nonce, Encoding.UTF8.GetBytes(associatedData)));
        var output = new byte[cipher.GetOutputSize(sealedData.Length - NonceSize)];
        try
        {
            var len = cipher.ProcessBytes(sealedData, NonceSize, sealedData.Length - NonceSize, output, 0);
            cipher.DoFinal(output, len);
        }
        catch (Org.BouncyCastle.Crypto.InvalidCipherTextException e)
        {
            throw new CryptographicException("Decryption failed.", e);
        }
        return output;
    }

    /// <summary>Derives a subkey for a specific purpose from a key.</summary>
    public static byte[] Derive(byte[] key, string purpose, int length = KeySize) =>
        HKDF.DeriveKey(HashAlgorithmName.SHA256, key, length, info: Encoding.UTF8.GetBytes(purpose));

    public static byte[] Sha256(byte[] data) => SHA256.HashData(data);
}

public static class Base64Url
{
    public static string Encode(byte[] data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static byte[] Decode(string text)
    {
        var s = text.Replace('-', '+').Replace('_', '/');
        s += (s.Length % 4) switch { 2 => "==", 3 => "=", _ => "" };
        return Convert.FromBase64String(s);
    }
}
