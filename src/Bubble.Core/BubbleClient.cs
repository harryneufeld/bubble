namespace Bubble.Core;

/// <summary>Ein entschlüsselter Post, wie ihn der Feed anzeigt. <see cref="Id"/> ist die Nachrichten-ID auf dem Relay.</summary>
public sealed record FeedPost(string Id, BubbleMessage Message, bool IsMine);

/// <summary>Ein Mitglied, wie es sich selbst zuletzt per Profile-Nachricht beschrieben hat.</summary>
public sealed record Member(string Key, string Name, byte[]? Avatar, string? Bio, bool IsMe);

/// <summary>Ein Kommentar unter einem Post.</summary>
public sealed record Comment(string Id, string AuthorKey, string AuthorName, DateTimeOffset CreatedAt, string Text);

/// <summary>
/// Alles, was eine Bubble gerade zeigt: Posts (neueste zuerst), Mitglieder, Herzen und Kommentare.
/// Herzen und Kommentare lassen sich lokal ergänzen, damit die Oberfläche sofort reagiert.
/// </summary>
public sealed class BubbleSnapshot(
    IReadOnlyList<FeedPost> posts,
    IReadOnlyList<Member> members,
    string myKey,
    Dictionary<string, List<string>> likes,
    Dictionary<string, List<Comment>> comments)
{
    public IReadOnlyList<FeedPost> Posts => posts;
    public IReadOnlyList<Member> Members => members;

    public Member? MemberFor(string key) => members.FirstOrDefault(m => m.Key == key);

    /// <summary>Wer ein Herz gesetzt hat, in der Reihenfolge der Herzen.</summary>
    public IReadOnlyList<Member> LikersOf(string postId) => likes.GetValueOrDefault(postId) is { } keys
        ? keys.Select(k => MemberFor(k) ?? new Member(k, "?", null, null, k == myKey)).ToList()
        : [];

    public bool IsLikedByMe(string postId) => likes.GetValueOrDefault(postId)?.Contains(myKey) == true;

    public IReadOnlyList<Comment> CommentsOf(string postId) => comments.GetValueOrDefault(postId) ?? [];

    public void SetLikeByMe(string postId, bool liked)
    {
        if (!likes.TryGetValue(postId, out var keys)) likes[postId] = keys = [];
        keys.Remove(myKey);
        if (liked) keys.Add(myKey);
    }

    public void AddComment(string postId, Comment comment)
    {
        if (!comments.TryGetValue(postId, out var list)) comments[postId] = list = [];
        list.Add(comment);
    }
}

/// <summary>
/// Fachlogik einer Bubble: Feed laden, posten, Herzen und Kommentare, Lebensdauer ändern, Profil verteilen.
/// Plattformunabhängig, damit App und Tests denselben Code nutzen.
/// </summary>
public sealed class BubbleClient(Vault vault, Func<string, RelayClient> relayFor, TimeProvider? time = null)
{
    public const int MaxCommentLength = 500;

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
        var postIds = posts.Select(p => p.Id).ToHashSet();

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

        // Herzen: pro Person und Post zählt die neueste Nachricht
        var likes = messages
            .Where(m => m.Message.Kind == MessageKind.Like && m.Message.TargetId is { } t && postIds.Contains(t))
            .GroupBy(m => (Post: m.Message.TargetId!, m.Message.AuthorKey))
            .Select(g => g.Select(m => m.Message).MaxBy(m => m.CreatedAt)!)
            .Where(m => m.Active == true)
            .OrderBy(m => m.CreatedAt)
            .GroupBy(m => m.TargetId!)
            .ToDictionary(g => g.Key, g => g.Select(m => m.AuthorKey).ToList());

        var comments = messages
            .Where(m => m.Message.Kind == MessageKind.Comment && m.Message.TargetId is { } t && postIds.Contains(t) && !string.IsNullOrWhiteSpace(m.Message.Text))
            .OrderBy(m => m.Message.CreatedAt)
            .GroupBy(m => m.Message.TargetId!)
            .ToDictionary(g => g.Key, g => g
                .Select(m => new Comment(m.Id, m.Message.AuthorKey, m.Message.AuthorName, m.Message.CreatedAt, m.Message.Text!))
                .ToList());

        return new BubbleSnapshot(posts, members, _identity.PublicKey, likes, comments);
    }

    public async Task<IReadOnlyList<FeedPost>> LoadFeedAsync(BubbleMembership bubble, CancellationToken ct = default) =>
        (await LoadAsync(bubble, ct)).Posts;

    /// <summary>Teilt ein Highlight mit einem Emoji für das Gefühl. Es gibt kein Tageslimit.</summary>
    public async Task PostAsync(BubbleMembership bubble, string? emoji, string? text, byte[]? image, CancellationToken ct = default)
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
            Emoji = string.IsNullOrWhiteSpace(emoji) ? null : emoji.Trim(),
            Text = string.IsNullOrWhiteSpace(text) ? null : text.Trim(),
            Image = image,
        }, ct);
    }

    /// <summary>Setzt oder nimmt das Herz an einem Post zurück. Wirft, wenn der Post schon abgelaufen ist.</summary>
    public async Task LikeAsync(BubbleMembership bubble, FeedPost post, bool liked, CancellationToken ct = default)
    {
        EnsureOpen(post);
        await SendAsync(bubble, new BubbleMessage
        {
            Kind = MessageKind.Like,
            AuthorKey = _identity.PublicKey,
            AuthorName = vault.DisplayName,
            CreatedAt = _time.GetUtcNow(),
            Lifetime = PostLifetime.Persistent,
            TargetId = post.Id,
            Active = liked,
            TargetExpiresAt = post.Message.ExpiresAt,
        }, ct);
    }

    /// <summary>Kommentiert einen Post. Der Kommentar verschwindet zusammen mit dem Post.</summary>
    public async Task<Comment> CommentAsync(BubbleMembership bubble, FeedPost post, string text, CancellationToken ct = default)
    {
        text = text.Trim();
        if (text.Length == 0) throw new ArgumentException("Ein Kommentar braucht Text.", nameof(text));
        if (text.Length > MaxCommentLength) text = text[..MaxCommentLength];
        EnsureOpen(post);

        var createdAt = _time.GetUtcNow();
        var id = await SendAsync(bubble, new BubbleMessage
        {
            Kind = MessageKind.Comment,
            AuthorKey = _identity.PublicKey,
            AuthorName = vault.DisplayName,
            CreatedAt = createdAt,
            Lifetime = PostLifetime.Persistent,
            TargetId = post.Id,
            Text = text,
            TargetExpiresAt = post.Message.ExpiresAt,
        }, ct);
        return new Comment(id, _identity.PublicKey, vault.DisplayName, createdAt, text);
    }

    void EnsureOpen(FeedPost post)
    {
        if (post.Message.ExpiresAt is { } expires && expires <= _time.GetUtcNow())
            throw new InvalidOperationException("Dieses Highlight ist schon verschwunden.");
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

    async Task<string> SendAsync(BubbleMembership bubble, BubbleMessage message, CancellationToken ct)
    {
        var encrypted = MessageCodec.Encrypt(bubble, _identity, message);
        await relayFor(bubble.RelayUrl).PostMessageAsync(bubble.MailboxId, encrypted, ct);
        return encrypted.Id;
    }
}
