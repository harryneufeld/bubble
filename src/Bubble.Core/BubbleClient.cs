namespace Bubble.Core;

/// <summary>Ein entschlüsselter Post, wie ihn der Feed anzeigt.</summary>
public sealed record FeedPost(BubbleMessage Message, bool IsMine);

/// <summary>
/// Fachlogik einer Bubble: Feed laden, posten (1 Highlight pro Tag), Lebensdauer ändern.
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
    public async Task<IReadOnlyList<FeedPost>> LoadFeedAsync(BubbleMembership bubble, CancellationToken ct = default)
    {
        var raw = await relayFor(bubble.RelayUrl).GetMessagesAsync(bubble.MailboxId, ct: ct);
        var messages = raw.Select(r => MessageCodec.Decrypt(bubble, r)).OfType<BubbleMessage>().ToList();

        var lastSetting = messages.Where(m => m.Kind == MessageKind.LifetimeChanged).MaxBy(m => m.CreatedAt);
        if (lastSetting is not null) bubble.Lifetime = lastSetting.Lifetime;

        var now = _time.GetUtcNow();
        return messages
            .Where(m => m.Kind == MessageKind.Post && (m.ExpiresAt is null || m.ExpiresAt > now))
            .OrderByDescending(m => m.CreatedAt)
            .Select(m => new FeedPost(m, m.AuthorKey == _identity.PublicKey))
            .ToList();
    }

    public bool HasPostedToday(IEnumerable<FeedPost> feed, TimeZoneInfo? zone = null)
    {
        zone ??= _time.LocalTimeZone;
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(_time.GetUtcNow(), zone).Date);
        return feed.Any(p => p.IsMine && DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(p.Message.CreatedAt, zone).Date) == today);
    }

    /// <summary>Teilt das Highlight des Tages. Wirft, wenn heute schon gepostet wurde.</summary>
    public async Task PostAsync(BubbleMembership bubble, Mood mood, string? text, byte[]? image, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text) && image is null)
            throw new ArgumentException("Ein Post braucht Text oder ein Bild.");

        var feed = await LoadFeedAsync(bubble, ct);
        if (HasPostedToday(feed))
            throw new InvalidOperationException("Du hast heute in dieser Bubble schon dein Highlight geteilt.");

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

    Task SendAsync(BubbleMembership bubble, BubbleMessage message, CancellationToken ct) =>
        relayFor(bubble.RelayUrl).PostMessageAsync(bubble.MailboxId, MessageCodec.Encrypt(bubble, _identity, message), ct);
}
