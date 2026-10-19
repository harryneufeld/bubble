using System.Text.Json;
using System.Text.Json.Serialization;

namespace Bubble.Core;

/// <summary>Wie lange Posts einer Bubble leben.</summary>
public enum PostLifetime { Story24h, Persistent }

public enum Mood { Top, Flop }

public enum MessageKind { Post, LifetimeChanged }

/// <summary>
/// Eine Bubble aus Sicht eines Mitglieds. Der Root-Key ist das gemeinsame Geheimnis;
/// Mailbox-ID und Inhaltsschlüssel werden daraus abgeleitet, sodass der Relay
/// nur eine zufällig aussehende Mailbox-ID sieht.
/// </summary>
public sealed class BubbleMembership
{
    public required string Name { get; set; }
    public required string Key { get; set; }
    public required string RelayUrl { get; set; }
    public PostLifetime Lifetime { get; set; }

    [JsonIgnore] public string MailboxId => Base64Url.Encode(Crypto.Derive(Base64Url.Decode(Key), "bubble/v1/mailbox"));
    [JsonIgnore] internal byte[] ContentKey => Crypto.Derive(Base64Url.Decode(Key), "bubble/v1/content");

    public static BubbleMembership Create(string name, string relayUrl, PostLifetime lifetime) => new()
    {
        Name = name,
        Key = Base64Url.Encode(Crypto.NewKey()),
        RelayUrl = relayUrl,
        Lifetime = lifetime,
    };
}

/// <summary>Klartext-Inhalt einer Nachricht. Liegt nur verschlüsselt auf dem Relay.</summary>
public sealed record BubbleMessage
{
    public required MessageKind Kind { get; init; }
    public required string AuthorKey { get; init; }
    public required string AuthorName { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>Bei Posts: die Lebensdauer, unter der geteilt wurde. Bei LifetimeChanged: die neue Einstellung.</summary>
    public required PostLifetime Lifetime { get; init; }

    public Mood? Mood { get; init; }
    public string? Text { get; init; }

    /// <summary>JPEG, bereits auf dem Gerät von Metadaten befreit.</summary>
    public byte[]? Image { get; init; }

    public DateTimeOffset? ExpiresAt =>
        Kind == MessageKind.Post && Lifetime == PostLifetime.Story24h ? CreatedAt.AddHours(24) : null;
}

/// <summary>Lokaler Account-Zustand. Liegt im sicheren Speicher des Geräts und (verschlüsselt) im Backup.</summary>
public sealed class Vault
{
    public int Version { get; set; } = 1;
    public required string IdentitySeed { get; set; }
    public required string DisplayName { get; set; }
    public List<BubbleMembership> Bubbles { get; set; } = [];

    /// <summary>Gesetzt, sobald ein Backup eingerichtet wurde; damit wird es bei Änderungen automatisch aktualisiert.</summary>
    public BackupState? Backup { get; set; }

    [JsonIgnore] public Identity Identity => Identity.FromSeed(Base64Url.Decode(IdentitySeed));

    public static Vault Create(string displayName) => new()
    {
        IdentitySeed = Base64Url.Encode(Identity.Create().Seed),
        DisplayName = displayName,
    };
}

// --- Relay-Protokoll (gemeinsam für Server und Client) ---

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
