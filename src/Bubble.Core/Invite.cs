using System.Text.Json;

namespace Bubble.Core;

/// <summary>
/// Einladung in eine Bubble. Der Schlüssel steht im Fragment (#...) des Links;
/// Fragmente werden von Browsern nie an Server geschickt.
/// Wer den Link hat, ist in der Bubble – also nur über vertrauenswürdige Kanäle teilen.
/// </summary>
public sealed record Invite(string Name, string Key, string RelayUrl, PostLifetime Lifetime)
{
    const string Prefix = "bubble://join#";

    public static Invite For(BubbleMembership bubble) => new(bubble.Name, bubble.Key, bubble.RelayUrl, bubble.Lifetime);

    public string ToLink() => Prefix + Base64Url.Encode(JsonSerializer.SerializeToUtf8Bytes(this, CoreJson.Default.Invite));

    public static bool TryParse(string text, out Invite? invite)
    {
        invite = null;
        var trimmed = text.Trim();
        var hash = trimmed.IndexOf('#');
        var fragment = hash >= 0 ? trimmed[(hash + 1)..] : trimmed;
        try
        {
            invite = JsonSerializer.Deserialize(Base64Url.Decode(fragment), CoreJson.Default.Invite);
            return invite is not null && Base64Url.Decode(invite.Key).Length == Crypto.KeySize;
        }
        catch (Exception e) when (e is FormatException or JsonException)
        {
            invite = null;
            return false;
        }
    }

    public BubbleMembership ToMembership() => new() { Name = Name, Key = Key, RelayUrl = RelayUrl, Lifetime = Lifetime };
}
