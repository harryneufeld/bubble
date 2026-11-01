using System.Security.Cryptography;
using System.Text.Json;

namespace Bubble.Core;

/// <summary>Signed message: the payload is the JSON of a <see cref="BubbleMessage"/>.</summary>
public sealed record SignedMessage(byte[] Payload, byte[] Signature);

/// <summary>
/// Packs messages for the relay: first sign with the identity, then encrypt with the
/// bubble's content key. Mailbox and message ID are bound as associated data,
/// so the server cannot move messages elsewhere.
/// </summary>
public static class MessageCodec
{
    public static RelayMessageIn Encrypt(BubbleMembership bubble, Identity author, BubbleMessage message)
    {
        if (message.AuthorKey != author.PublicKey)
            throw new ArgumentException("AuthorKey does not match the identity.", nameof(message));

        var payload = JsonSerializer.SerializeToUtf8Bytes(message, CoreJson.Default.BubbleMessage);
        var signed = JsonSerializer.SerializeToUtf8Bytes(new SignedMessage(payload, author.Sign(payload)), CoreJson.Default.SignedMessage);

        var id = Base64Url.Encode(Crypto.RandomBytes(16));
        var ciphertext = Crypto.Seal(bubble.ContentKey, signed, Aad(bubble, id));
        var parent = message.Kind is MessageKind.Like or MessageKind.Comment or MessageKind.PostLifetimeChanged ? message.TargetId : null;
        return new RelayMessageIn(id, Convert.ToBase64String(ciphertext), message.ExpiresAt?.ToUnixTimeSeconds(),
            SecretHash(author.MessageSecret(id)), parent);
    }

    /// <summary>What the relay stores to recognize the author later (Base64 of SHA-256 of the secret).</summary>
    public static string SecretHash(string secret) => Convert.ToBase64String(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(secret)));

    /// <summary>Returns null if the message cannot be decrypted or verified.</summary>
    public static BubbleMessage? Decrypt(BubbleMembership bubble, RelayMessageOut raw)
    {
        try
        {
            var signedJson = Crypto.Open(bubble.ContentKey, Convert.FromBase64String(raw.Ciphertext), Aad(bubble, raw.Id));
            var signed = JsonSerializer.Deserialize(signedJson, CoreJson.Default.SignedMessage);
            if (signed is null) return null;

            var message = JsonSerializer.Deserialize(signed.Payload, CoreJson.Default.BubbleMessage);
            if (message is null || !Identity.Verify(message.AuthorKey, signed.Payload, signed.Signature))
                return null;
            // Frühere Versionen schrieben für „kein Bild“ einen leeren Wert; der darf nicht als Foto zählen.
            return message.Image is { Length: 0 } ? message with { Image = null } : message;
        }
        catch (Exception e) when (e is CryptographicException or JsonException or FormatException)
        {
            return null;
        }
    }

    static string Aad(BubbleMembership bubble, string id) => $"bubble/v1/message|{bubble.MailboxId}|{id}";
}
