using System.Security.Cryptography;
using System.Text.Json;

namespace Bubble.Core;

/// <summary>Signierte Nachricht: Payload ist das JSON einer <see cref="BubbleMessage"/>.</summary>
public sealed record SignedMessage(byte[] Payload, byte[] Signature);

/// <summary>
/// Verpackt Nachrichten für den Relay: erst mit der Identität signieren, dann mit dem
/// Inhaltsschlüssel der Bubble verschlüsseln. Mailbox und Nachrichten-ID sind als
/// Associated Data gebunden, damit der Server Nachrichten nicht umhängen kann.
/// </summary>
public static class MessageCodec
{
    public static RelayMessageIn Encrypt(BubbleMembership bubble, Identity author, BubbleMessage message)
    {
        if (message.AuthorKey != author.PublicKey)
            throw new ArgumentException("AuthorKey passt nicht zur Identität.", nameof(message));

        var payload = JsonSerializer.SerializeToUtf8Bytes(message, CoreJson.Default.BubbleMessage);
        var signed = JsonSerializer.SerializeToUtf8Bytes(new SignedMessage(payload, author.Sign(payload)), CoreJson.Default.SignedMessage);

        var id = Base64Url.Encode(Crypto.RandomBytes(16));
        var ciphertext = Crypto.Seal(bubble.ContentKey, signed, Aad(bubble, id));
        return new RelayMessageIn(id, Convert.ToBase64String(ciphertext), message.ExpiresAt?.ToUnixTimeSeconds());
    }

    /// <summary>Gibt null zurück, wenn die Nachricht nicht entschlüsselt oder verifiziert werden kann.</summary>
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
            return message;
        }
        catch (Exception e) when (e is CryptographicException or JsonException or FormatException)
        {
            return null;
        }
    }

    static string Aad(BubbleMembership bubble, string id) => $"bubble/v1/message|{bubble.MailboxId}|{id}";
}
