namespace Bubble.Core;

/// <summary>Ein entschlüsselter Post, wie ihn der Feed anzeigt. <see cref="Id"/> ist die Nachrichten-ID auf dem Relay.</summary>
public sealed record FeedPost(string Id, BubbleMessage Message, bool IsMine);

/// <summary>Ein Mitglied, wie es sich selbst zuletzt per Profile-Nachricht beschrieben hat.</summary>
public sealed record Member(string Key, string Name, byte[]? Avatar, string? Bio, bool IsMe);

/// <summary>Alles, was eine Bubble gerade zeigt: Posts (neueste zuerst) und Mitglieder.</summary>
public sealed record BubbleSnapshot(IReadOnlyList<FeedPost> Posts, IReadOnlyList<Member> Members)
{
    public Member? MemberFor(string key) => Members.FirstOrDefault(m => m.Key == key);
}

/// <summary>
/// Fachlogik einer Bubble: Feed laden, posten, Lebensdauer ändern, Profil verteilen.
/// Plattformunabhängig, damit App und Tests denselben Code nutzen.
/// </summary>
public sealed class BubbleClient(Vault vault, Func<string, RelayClient> relayFor, TimeProvider? time = null)
{
    readonly TimeProvider _time = time ?? TimeProvider.System;
    readonly Identity _identity = vault.Identity;

    /// <summary>
    /// Lädt alle Nachrichten der Bubble. Übernimmt dabei die zuletzt gesetzte Lebensdauer
    /// in <paramref name="bubble"/> (der Aufrufer speichert danach den Vault).
    /// </summary>
    public async Task<BubbleSnapshot> LoadAsync(BubbleMembership bubble, CancellationToken ct = default)
    {
        var raw = await relayFor(bubble.RelayUrl).GetMessagesAsync(bubble.MailboxId, ct: ct);
        var messages = raw
            .Select(r => (r.Id, Message: MessageCodec.Decrypt(bubble, r)))
            .Where(x => x.Message is not null)
            .Select(x => (x.Id, Message: x.Message!))
            .ToList();

        var lastSetting = messages.Select(m => m.Message).Where(m => m.Kind == MessageKind.LifetimeChanged).MaxBy(m => m.CreatedAt);
        if (lastSetting is not null) bubble.Lifetime = lastSetting.Lifetime;

        var now = _time.GetUtcNow();
        var posts = messages
            .Where(m => m.Message.Kind == MessageKind.Post && (m.Message.ExpiresAt is null || m.Message.ExpiresAt > now))
            .OrderByDescending(m => m.Message.CreatedAt)
            .Select(m => new FeedPost(m.Id, m.Message, m.Message.AuthorKey == _identity.PublicKey))
            .ToList();

        // Mitglieder: wer ein Profil geschickt oder etwas geteilt hat; die jeweils neueste Angabe gewinnt.
        var members = messages
            .GroupBy(m => m.Message.AuthorKey)
            .Select(g =>
            {
                var profile = g.Select(m => m.Message).Where(m => m.Kind == MessageKind.Profile).MaxBy(m => m.CreatedAt);
                var latest = g.Select(m => m.Message).MaxBy(m => m.CreatedAt)!;
                return new Member(g.Key, (profile ?? latest).AuthorName, profile?.Image, profile?.Bio, g.Key == _identity.PublicKey);
            })
            .OrderByDescending(m => m.IsMe).ThenBy(m => m.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        return new BubbleSnapshot(posts, members);
    }

    public async Task<IReadOnlyList<FeedPost>> LoadFeedAsync(BubbleMembership bubble, CancellationToken ct = default) =>
        (await LoadAsync(bubble, ct)).Posts;

    /// <summary>Teilt ein Highlight. Es gibt kein Tageslimit.</summary>
    public async Task PostAsync(BubbleMembership bubble, Mood mood, string? text, byte[]? image, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text) && image is null)
            throw new ArgumentException("Ein Post braucht Text oder ein Bild.");

        await SendAsync(bubble, new BubbleMessage
        {
            Kind = MessageKind.Post,
            AuthorKey = _identity.PublicKey,
            AuthorName = vault.DisplayName,
            CreatedAt = _time.GetUtcNow(),
            Lifetime = bubble.Lifetime,
            Mood = mood,
            Text = string.IsNullOrWhiteSpace(text) ? null : text.Trim(),
            Image = image,
        }, ct);
    }

    /// <summary>Ändert die Lebensdauer für künftige Posts. Bestehende Posts behalten ihre Regel.</summary>
    public async Task SetLifetimeAsync(BubbleMembership bubble, PostLifetime lifetime, CancellationToken ct = default)
    {
        await SendAsync(bubble, new BubbleMessage
        {
            Kind = MessageKind.LifetimeChanged,
            AuthorKey = _identity.PublicKey,
            AuthorName = vault.DisplayName,
            CreatedAt = _time.GetUtcNow(),
            Lifetime = lifetime,
        }, ct);
        bubble.Lifetime = lifetime;
    }

    /// <summary>Schickt Name, Profilbild und Bio an die Bubble. Nach dem Beitreten und nach jeder Profiländerung.</summary>
    public Task PublishProfileAsync(BubbleMembership bubble, CancellationToken ct = default) =>
        SendAsync(bubble, new BubbleMessage
        {
            Kind = MessageKind.Profile,
            AuthorKey = _identity.PublicKey,
            AuthorName = vault.DisplayName,
            CreatedAt = _time.GetUtcNow(),
            Lifetime = PostLifetime.Persistent,
            Image = vault.Avatar,
            Bio = vault.Bio,
        }, ct);

    Task SendAsync(BubbleMembership bubble, BubbleMessage message, CancellationToken ct) =>
        relayFor(bubble.RelayUrl).PostMessageAsync(bubble.MailboxId, MessageCodec.Encrypt(bubble, _identity, message), ct);
}
