using System.Text.Json;

namespace Bubble.Core;

/// <summary>
/// Invitation to a bubble. The key lives in the fragment (#...) of the link;
/// browsers never send fragments to servers.
/// Whoever has the link is in the bubble – so only share it over trusted channels.
/// </summary>
public sealed record Invite(string Name, string Key, string RelayUrl, PostLifetime Lifetime, string? OwnerKey = null)
{
    const string Prefix = "bubble://join#";

    public static Invite For(BubbleMembership bubble) => new(bubble.Name, bubble.Key, bubble.RelayUrl, bubble.Lifetime, bubble.OwnerKey);

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

    public BubbleMembership ToMembership() => new() { Name = Name, Key = Key, RelayUrl = RelayUrl, Lifetime = Lifetime, OwnerKey = OwnerKey };
}
