using System.Text;
using System.Text.Json;

namespace Bubble.Core;

/// <summary>
/// Short invitation code to type in, e.g. "7KQ2M-9XH4T" (50 bits of randomness, valid for 24 h).
/// A mailbox ID and a key are derived from the code with Argon2id; in that mailbox the full
/// <see cref="Invite"/> sits encrypted on the relay as a 24h message.
/// The relay sees neither the code nor the bubble. The short validity and Argon2id protect against guessing.
/// </summary>
public static class ShortInvite
{
    const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
    const int CodeChars = 10;
    static readonly byte[] Salt = Encoding.UTF8.GetBytes("bubble/v1/short-invite");
    public static readonly TimeSpan Validity = TimeSpan.FromHours(24);

    public static string GenerateCode()
    {
        var chars = Crypto.RandomBytes(CodeChars).Select(b => Alphabet[b & 31]).ToArray();
        return $"{new string(chars, 0, 5)}-{new string(chars, 5, 5)}";
    }

    /// <summary>Capitalization, look-alikes (O→0, I/L→1) and separators are tolerated.</summary>
    public static bool TryNormalize(string? input, out string code)
    {
        var sb = new StringBuilder();
        foreach (var c in (input ?? "").ToUpperInvariant())
        {
            var mapped = c switch { 'O' => '0', 'I' or 'L' => '1', _ => c };
            if (Alphabet.Contains(mapped)) sb.Append(mapped);
        }
        code = sb.Length == CodeChars ? $"{sb.ToString(0, 5)}-{sb.ToString(5, 5)}" : "";
        return code.Length > 0;
    }

    /// <summary>Slow (Argon2id) – call on a background thread.</summary>
    public static (string MailboxId, byte[] Key) Derive(string code)
    {
        if (!TryNormalize(code, out var normalized))
            throw new FormatException("The invitation code does not have the right format.");
        var output = Backup.Argon2(Encoding.UTF8.GetBytes(normalized.Replace("-", "")), Salt, 64);
        return (Base64Url.Encode(output[..32]), output[32..]);
    }

    /// <summary>Stores the invitation under a new code and returns the code.</summary>
    public static async Task<string> PublishAsync(Invite invite, RelayClient relay, TimeProvider? time = null, CancellationToken ct = default)
    {
        var code = GenerateCode();
        var (mailbox, key) = await Task.Run(() => Derive(code), ct);
        var id = Base64Url.Encode(Crypto.RandomBytes(16));
        var json = JsonSerializer.SerializeToUtf8Bytes(invite, CoreJson.Default.Invite);
        var ciphertext = Crypto.Seal(key, json, Aad(mailbox, id));
        var expires = (time ?? TimeProvider.System).GetUtcNow().Add(Validity).ToUnixTimeSeconds();
        await relay.PostMessageAsync(mailbox, new RelayMessageIn(id, Convert.ToBase64String(ciphertext), expires), ct);
        return code;
    }

    /// <summary>Resolves a code. Null if it does not (or no longer) exist.</summary>
    public static async Task<Invite?> ResolveAsync(string code, RelayClient relay, CancellationToken ct = default)
    {
        var (mailbox, key) = await Task.Run(() => Derive(code), ct);
        foreach (var raw in await relay.GetMessagesAsync(mailbox, ct: ct))
        {
            try
            {
                var json = Crypto.Open(key, Convert.FromBase64String(raw.Ciphertext), Aad(mailbox, raw.Id));
                if (JsonSerializer.Deserialize(json, CoreJson.Default.Invite) is { } invite) return invite;
            }
            catch (Exception e) when (e is System.Security.Cryptography.CryptographicException or JsonException or FormatException)
            {
                // foreign or broken entry: ignore
            }
        }
        return null;
    }

    static string Aad(string mailbox, string id) => $"bubble/v1/short-invite|{mailbox}|{id}";
}
