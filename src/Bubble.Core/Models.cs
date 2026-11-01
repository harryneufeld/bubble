using System.Text.Json;
using System.Text.Json.Serialization;

namespace Bubble.Core;

/// <summary>How long a post lives. For a bubble it is the longest lifetime its posts may have.</summary>
public enum PostLifetime { Story24h, Persistent }

public enum Mood { Top, Flop }

/// <summary>
/// Post = highlight, LifetimeChanged = new maximum lifetime of the bubble (owner only),
/// PostLifetimeChanged = the author makes one of their posts permanent or 24h, Profile = name and picture of the sender,
/// Like = heart on a post (on/off), Comment = comment on a post,
/// KeyRotation = the owner moved the bubble to a new key (see <see cref="BubbleMessage.Wraps"/>),
/// Left = the sender left the bubble.
/// </summary>
public enum MessageKind { Post, LifetimeChanged, Profile, Like, Comment, KeyRotation, Left, PostLifetimeChanged }

/// <summary>
/// A bubble from a member's point of view. The root key is the shared secret;
/// the mailbox ID and content key are derived from it, so the relay only
/// sees a random-looking mailbox ID.
/// </summary>
public sealed class BubbleMembership
{
    public required string Name { get; set; }
    public required string Key { get; set; }
    public required string RelayUrl { get; set; }
    /// <summary>The longest lifetime posts in this bubble may have. Set by the owner; each post picks its own up to this.</summary>
    public PostLifetime Lifetime { get; set; }

    /// <summary>
    /// Public key of the member who created the bubble. Only the owner can remove members.
    /// Null for bubbles created before owners existed.
    /// </summary>
    public string? OwnerKey { get; set; }

    [JsonIgnore] public string MailboxId => Base64Url.Encode(Crypto.Derive(Base64Url.Decode(Key), "bubble/v1/mailbox"));
    [JsonIgnore] internal byte[] ContentKey => Crypto.Derive(Base64Url.Decode(Key), "bubble/v1/content");

    public static BubbleMembership Create(string name, string relayUrl, PostLifetime lifetime, string? ownerKey = null) => new()
    {
        Name = name,
        Key = Base64Url.Encode(Crypto.NewKey()),
        RelayUrl = relayUrl,
        Lifetime = lifetime,
        OwnerKey = ownerKey,
    };
}

/// <summary>Plaintext content of a message. Only ever stored encrypted on the relay.</summary>
public sealed record BubbleMessage
{
    public required MessageKind Kind { get; init; }
    public required string AuthorKey { get; init; }
    public required string AuthorName { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>For posts: the lifetime they were shared under. For LifetimeChanged and PostLifetimeChanged: the new setting.</summary>
    public required PostLifetime Lifetime { get; init; }

    /// <summary>Old posts: top or flop. New posts use <see cref="Emoji"/>.</summary>
    public Mood? Mood { get; init; }

    /// <summary>How the sender feels, a single emoji (posts only).</summary>
    public string? Emoji { get; init; }

    public string? Text { get; init; }

    /// <summary>For Like, Comment and PostLifetimeChanged: the message ID of the post.</summary>
    public string? TargetId { get; init; }

    /// <summary>For Like: true = heart set, false = heart taken back. The newest message counts.</summary>
    public bool? Active { get; init; }

    /// <summary>For Like, Comment and PostLifetimeChanged: when the post disappears – they disappear with it.</summary>
    public DateTimeOffset? TargetExpiresAt { get; init; }

    /// <summary>JPEG, already stripped of metadata on the device. For Profile: the profile picture.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public byte[]? Image { get; init; }

    /// <summary>For Profile: a short sentence about themselves.</summary>
    public string? Bio { get; init; }

    /// <summary>For KeyRotation: the new bubble key, sealed separately for each remaining member (member key → sealed key).</summary>
    public Dictionary<string, string>? Wraps { get; init; }

    public DateTimeOffset? ExpiresAt => Kind switch
    {
        MessageKind.Post when Lifetime == PostLifetime.Story24h => CreatedAt.AddHours(24),
        MessageKind.Like or MessageKind.Comment or MessageKind.PostLifetimeChanged => TargetExpiresAt,
        _ => null,
    };
}

/// <summary>Local account state. Lives in the device's secure storage and (encrypted) in the backup.</summary>
public sealed class Vault
{
    public int Version { get; set; } = 1;
    public required string IdentitySeed { get; set; }
    public required string DisplayName { get; set; }

    /// <summary>Square JPEG without metadata. Sent encrypted to every bubble.</summary>
    public byte[]? Avatar { get; set; }
    public string? Bio { get; set; }

    public List<BubbleMembership> Bubbles { get; set; } = [];

    /// <summary>What the user chose last when sharing a highlight; the next one starts with it.</summary>
    public PostLifetime DefaultPostLifetime { get; set; } = PostLifetime.Story24h;

    /// <summary>Set once a backup has been set up; from then on it is updated automatically on changes.</summary>
    public BackupState? Backup { get; set; }

    [JsonIgnore] public Identity Identity => Identity.FromSeed(Base64Url.Decode(IdentitySeed));

    public static Vault Create(string displayName) => new()
    {
        IdentitySeed = Base64Url.Encode(Identity.Create().Seed),
        DisplayName = displayName,
    };
}

// --- Relay protocol (shared by server and client) ---

/// <summary>
/// <paramref name="SecretHash"/> (Base64 of SHA-256 of the author's message secret) lets the author change the expiry or
/// delete the message later. <paramref name="ParentId"/> ties hearts and comments to their post, so they follow its expiry.
/// </summary>
public sealed record RelayMessageIn(string Id, string Ciphertext, long? ExpiresAt, string? SecretHash = null, string? ParentId = null);

/// <summary>New expiry of a message (Unix seconds); null = permanent.</summary>
public sealed record ExpiryBody(long? ExpiresAt);

public sealed record RelayMessageOut(long Seq, string Id, string Ciphertext, long? ExpiresAt);

public sealed record BackupBody(string Ciphertext);

/// <summary>
/// How a device wants to be woken. <see cref="PushTypes.UnifiedPush"/>: the distributor's <c>Endpoint</c> URL.
/// <see cref="PushTypes.Gateway"/>: the platform (<c>fcm</c>, <c>apns</c>) and the device <c>Token</c>; the relay
/// swaps the token for an opaque handle at its push gateway and never stores the token.
/// </summary>
public sealed record PushSubscriptionIn(string Type, string? Endpoint, List<string> Mailboxes, string? Platform = null, string? Token = null);

public sealed record PushSubscriptionOut(string Id, string Secret);

/// <summary>Platform and token are optional and only for gateway subscriptions: they renew the handle before it expires.</summary>
public sealed record PushMailboxesBody(List<string> Mailboxes, string? Platform = null, string? Token = null);

public static class PushTypes
{
    /// <summary>Woken through the relay's push gateway (FCM/APNs).</summary>
    public const string Gateway = "gateway";

    public const string UnifiedPush = "unifiedpush";

    public static bool IsValid(string type) => type is Gateway or UnifiedPush;
}

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, UseStringEnumConverter = true)]
[JsonSerializable(typeof(BubbleMessage))]
[JsonSerializable(typeof(SignedMessage))]
[JsonSerializable(typeof(Vault))]
[JsonSerializable(typeof(BackupBlob))]
[JsonSerializable(typeof(Invite))]
[JsonSerializable(typeof(RelayMessageIn))]
[JsonSerializable(typeof(ExpiryBody))]
[JsonSerializable(typeof(RelayMessageOut))]
[JsonSerializable(typeof(List<RelayMessageOut>))]
[JsonSerializable(typeof(BackupBody))]
[JsonSerializable(typeof(PushSubscriptionIn))]
[JsonSerializable(typeof(PushSubscriptionOut))]
[JsonSerializable(typeof(PushMailboxesBody))]
[JsonSerializable(typeof(Dictionary<string, PushRelayState>))]
public partial class CoreJson : JsonSerializerContext;
