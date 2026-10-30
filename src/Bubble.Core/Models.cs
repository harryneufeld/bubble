using System.Text.Json;
using System.Text.Json.Serialization;

namespace Bubble.Core;

/// <summary>How long the posts of a bubble live.</summary>
public enum PostLifetime { Story24h, Persistent }

public enum Mood { Top, Flop }

/// <summary>
/// Post = highlight, LifetimeChanged = new lifetime, Profile = name and picture of the sender,
/// Like = heart on a post (on/off), Comment = comment on a post,
/// KeyRotation = the owner moved the bubble to a new key (see <see cref="BubbleMessage.Wraps"/>),
/// Left = the sender left the bubble.
/// </summary>
public enum MessageKind { Post, LifetimeChanged, Profile, Like, Comment, KeyRotation, Left }

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

    /// <summary>For posts: the lifetime they were shared under. For LifetimeChanged: the new setting.</summary>
    public required PostLifetime Lifetime { get; init; }

    /// <summary>Old posts: top or flop. New posts use <see cref="Emoji"/>.</summary>
    public Mood? Mood { get; init; }

    /// <summary>How the sender feels, a single emoji (posts only).</summary>
    public string? Emoji { get; init; }

    public string? Text { get; init; }

    /// <summary>For Like and Comment: the message ID of the post.</summary>
    public string? TargetId { get; init; }

    /// <summary>For Like: true = heart set, false = heart taken back. The newest message counts.</summary>
    public bool? Active { get; init; }

    /// <summary>For Like and Comment: when the post disappears – hearts and comments disappear with it.</summary>
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
        MessageKind.Like or MessageKind.Comment => TargetExpiresAt,
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

public sealed record RelayMessageIn(string Id, string Ciphertext, long? ExpiresAt);

public sealed record RelayMessageOut(long Seq, string Id, string Ciphertext, long? ExpiresAt);

public sealed record BackupBody(string Ciphertext);

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, UseStringEnumConverter = true)]
[JsonSerializable(typeof(BubbleMessage))]
[JsonSerializable(typeof(SignedMessage))]
[JsonSerializable(typeof(Vault))]
[JsonSerializable(typeof(BackupBlob))]
[JsonSerializable(typeof(Invite))]
[JsonSerializable(typeof(RelayMessageIn))]
[JsonSerializable(typeof(RelayMessageOut))]
[JsonSerializable(typeof(List<RelayMessageOut>))]
[JsonSerializable(typeof(BackupBody))]
public partial class CoreJson : JsonSerializerContext;
