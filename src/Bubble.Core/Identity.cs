using System.Numerics;
using System.Security.Cryptography;
using Org.BouncyCastle.Crypto.Agreement;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;

namespace Bubble.Core;

/// <summary>
/// The identity of an account: an Ed25519 key pair, no email, no phone number.
/// The public key (Base64Url) is also the member ID.
/// </summary>
public sealed class Identity
{
    readonly Ed25519PrivateKeyParameters _privateKey;

    Identity(byte[] seed) => _privateKey = new Ed25519PrivateKeyParameters(seed);

    public static Identity Create() => new(Crypto.NewKey());

    public static Identity FromSeed(byte[] seed) => new(seed);

    public byte[] Seed => _privateKey.GetEncoded();

    public string PublicKey => Base64Url.Encode(_privateKey.GeneratePublicKey().GetEncoded());

    public byte[] Sign(byte[] message)
    {
        var signer = new Ed25519Signer();
        signer.Init(true, _privateKey);
        signer.BlockUpdate(message, 0, message.Length);
        return signer.GenerateSignature();
    }

    /// <summary>
    /// Diffie-Hellman with another member: the Ed25519 keys are converted to X25519 (the usual birational map).
    /// Both sides get the same secret, which nobody else – including the relay – can compute.
    /// </summary>
    public byte[] SharedSecretWith(string otherPublicKey)
    {
        var scalar = SHA512.HashData(Seed)[..32];
        var agreement = new X25519Agreement();
        agreement.Init(new X25519PrivateKeyParameters(scalar));
        var secret = new byte[agreement.AgreementSize];
        agreement.CalculateAgreement(new X25519PublicKeyParameters(ToMontgomery(Base64Url.Decode(otherPublicKey))), secret, 0);
        return secret;
    }

    static readonly BigInteger P = (BigInteger.One << 255) - 19;

    static byte[] ToMontgomery(byte[] edPublicKey)
    {
        if (edPublicKey.Length != 32) throw new ArgumentException("Invalid public key.");
        var bytes = (byte[])edPublicKey.Clone();
        bytes[31] &= 0x7F;
        var y = new BigInteger(bytes, isUnsigned: true, isBigEndian: false);
        var denominator = ((1 - y) % P + P) % P;
        var u = (1 + y) % P * BigInteger.ModPow(denominator, P - 2, P) % P;
        var result = new byte[32];
        u.TryWriteBytes(result, out _, isUnsigned: true, isBigEndian: false);
        return result;
    }

    public static bool Verify(string publicKey, byte[] message, byte[] signature)
    {
        try
        {
            var signer = new Ed25519Signer();
            signer.Init(false, new Ed25519PublicKeyParameters(Base64Url.Decode(publicKey)));
            signer.BlockUpdate(message, 0, message.Length);
            return signer.VerifySignature(signature);
        }
        catch (Exception e) when (e is ArgumentException or FormatException)
        {
            return false;
        }
    }
}
