using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;

namespace Bubble.Core;

/// <summary>
/// Die Identität eines Accounts: ein Ed25519-Schlüsselpaar, keine E-Mail, keine Telefonnummer.
/// Der Public Key (Base64Url) ist gleichzeitig die Member-ID.
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
