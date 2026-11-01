using System.Security.Cryptography;

namespace Bubble.Core;

/// <summary>A decrypted post as the feed shows it. <see cref="Id"/> is the message ID on the relay.</summary>
public sealed record FeedPost(string Id, BubbleMessage Message, bool IsMine)
{
    /// <summary>When the post disappears, after the author's changes and the bubble's maximum. Null = stays.</summary>
    public DateTimeOffset? ExpiresAt { get; init; } = Message.ExpiresAt;
}

/// <summary>A member as they last described themselves in a Profile message.</summary>
public sealed record Member(string Key, string Name, byte[]? Avatar, string? Bio, bool IsMe);

/// <summary>A comment under a post.</summary>
public sealed record Comment(string Id, string AuthorKey, string AuthorName, DateTimeOffset CreatedAt, string Text);

/// <summary>
/// Everything a bubble currently shows: posts (newest first), members, hearts and comments.
/// Hearts and comments can be added locally so the UI reacts immediately.
/// </summary>
public sealed class BubbleSnapshot(
    IReadOnlyList<FeedPost> posts,
    IReadOnlyList<Member> members,
    string myKey,
    Dictionary<string, List<string>> likes,
    Dictionary<string, List<Comment>> comments)
{
    public IReadOnlyList<FeedPost> Posts => posts;

    /// <summary>My own member key.</summary>
    public string MyKey => myKey;
    public IReadOnlyList<Member> Members => members;

    /// <summary>True if the owner removed me from this bubble (the key was rotated without me).</summary>
    public bool Removed { get; init; }

    public Member? MemberFor(string key) => members.FirstOrDefault(m => m.Key == key);

    /// <summary>Who set a heart, in the order of the hearts.</summary>
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
/// The logic of a bubble: load the feed, post, hearts and comments, change the lifetime, share the profile,
/// remove members. Platform independent so the app and the tests use the same code.
/// </summary>
public sealed class BubbleClient(Vault vault, Func<string, RelayClient> relayFor, TimeProvider? time = null, IMessageCache? cache = null)
{
    public const int MaxCommentLength = 500;
    const int MaxRotationHops = 8;

    readonly TimeProvider _time = time ?? TimeProvider.System;
    readonly Identity _identity = vault.Identity;

    /// <summary>
    /// Loads all messages of the bubble. Adopts the most recent lifetime setting into
    /// <paramref name="bubble"/>, and follows a key rotation by the owner (the caller saves the vault afterwards).
    /// </summary>
    public async Task<BubbleSnapshot> LoadAsync(BubbleMembership bubble, CancellationToken ct = default)
    {
        for (var hop = 0; ; hop++)
        {
            var (snapshot, newKey) = await LoadOnceAsync(bubble, ct);
            if (newKey is null || hop >= MaxRotationHops) return snapshot!;
            bubble.Key = Base64Url.Encode(newKey);
            // The new mailbox starts empty: introduce myself again so I stay in the member list.
            try { await PublishProfileAsync(bubble, ct); }
            catch (HttpRequestException) { /* retried implicitly the next time I share something */ }
        }
    }

    /// <summary>What the bubble looked like when it was last loaded, without asking the relay. Null if nothing is cached.</summary>
    public async Task<BubbleSnapshot?> LoadCachedAsync(BubbleMembership bubble, CancellationToken ct = default) =>
        cache?.Load(bubble.MailboxId) is { } raw ? (await BuildAsync(bubble, raw, sync: false, ct)).Snapshot : null;

    async Task<(BubbleSnapshot? Snapshot, byte[]? NewKey)> LoadOnceAsync(BubbleMembership bubble, CancellationToken ct)
    {
        var raw = await relayFor(bubble.RelayUrl).GetMessagesAsync(bubble.MailboxId, ct: ct);
        cache?.Save(bubble.MailboxId, raw);
        return await BuildAsync(bubble, raw, sync: true, ct);
    }

    async Task<(BubbleSnapshot? Snapshot, byte[]? NewKey)> BuildAsync(BubbleMembership bubble, List<RelayMessageOut> raw, bool sync, CancellationToken ct)
    {
        var messages = raw
            .Select(r => (r.Id, Message: MessageCodec.Decrypt(bubble, r)))
            .Where(x => x.Message is not null)
            .Select(x => (x.Id, Message: x.Message!))
            .ToList();

        // The owner moved the bubble to a new key: follow it if the new key was sealed for me, otherwise I was removed.
        var removed = false;
        if (bubble.OwnerKey is { } owner)
        {
            var rotation = messages.Select(m => m.Message)
                .Where(m => m.Kind == MessageKind.KeyRotation && m.AuthorKey == owner)
                .MaxBy(m => m.CreatedAt);
            if (rotation is not null)
            {
                var newKey = TryUnwrapKey(bubble, owner, rotation);
                if (newKey is not null) return (null, newKey);
                removed = true;
            }
        }

        // Only the owner sets the maximum (bubbles from before owners existed: any member).
        var lastSetting = messages.Select(m => m.Message)
            .Where(m => m.Kind == MessageKind.LifetimeChanged && (bubble.OwnerKey is null || m.AuthorKey == bubble.OwnerKey))
            .MaxBy(m => m.CreatedAt);
        if (lastSetting is not null) bubble.Lifetime = lastSetting.Lifetime;

        var now = _time.GetUtcNow();
        var changes = messages.Select(m => m.Message).Where(m => m.Kind == MessageKind.PostLifetimeChanged && m.TargetId is not null).ToList();
        var posts = messages
            .Where(m => m.Message.Kind == MessageKind.Post)
            .Select(m => new FeedPost(m.Id, m.Message, m.Message.AuthorKey == _identity.PublicKey)
            {
                ExpiresAt = EffectiveExpiry(m.Id, m.Message, changes, bubble.Lifetime, lastSetting?.CreatedAt),
            })
            .Where(p => p.ExpiresAt is null || p.ExpiresAt > now)
            .OrderByDescending(p => p.Message.CreatedAt)
            .ToList();
        if (sync) await SyncExpiryAsync(bubble, posts, raw.ToDictionary(r => r.Id, r => r.ExpiresAt), ct);
        var postIds = posts.Select(p => p.Id).ToHashSet();

        // Members: whoever sent a profile or shared something, unless their latest message says they left; the newest details win.
        var members = messages
            .GroupBy(m => m.Message.AuthorKey)
            .Where(g => !HasLeft(g.Select(m => m.Message).ToList()))
            .Select(g =>
            {
                var profile = g.Select(m => m.Message).Where(m => m.Kind == MessageKind.Profile).MaxBy(m => m.CreatedAt);
                var latest = g.Select(m => m.Message).MaxBy(m => m.CreatedAt)!;
                return new Member(g.Key, (profile ?? latest).AuthorName, profile?.Image, profile?.Bio, g.Key == _identity.PublicKey);
            })
            .OrderByDescending(m => m.IsMe).ThenBy(m => m.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        // Hearts: per person and post, the newest message counts
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

        return (new BubbleSnapshot(posts, members, _identity.PublicKey, likes, comments) { Removed = removed }, null);
    }

    /// <summary>
    /// The author's latest change wins over what the post was shared with. If the bubble allows only 24h,
    /// a permanent post disappears 24h after the owner set that (or after the post, if that is later).
    /// </summary>
    static DateTimeOffset? EffectiveExpiry(string id, BubbleMessage post, List<BubbleMessage> changes, PostLifetime max, DateTimeOffset? maxSince)
    {
        var change = changes
            .Where(c => c.TargetId == id && c.AuthorKey == post.AuthorKey && c.CreatedAt >= post.CreatedAt)
            .MaxBy(c => c.CreatedAt);
        var lifetime = change?.Lifetime ?? post.Lifetime;
        var since = change?.CreatedAt ?? post.CreatedAt;
        if (lifetime == PostLifetime.Story24h) return since.AddHours(24);
        if (max == PostLifetime.Persistent) return null;
        return (maxSince is { } s && s > since ? s : since).AddHours(24);
    }

    /// <summary>Brings the relay's expiry of my own posts in line with what the rules above say. Best effort.</summary>
    async Task SyncExpiryAsync(BubbleMembership bubble, List<FeedPost> posts, Dictionary<string, long?> onRelay, CancellationToken ct)
    {
        foreach (var post in posts.Where(p => p.IsMine))
        {
            var wanted = post.ExpiresAt?.ToUnixTimeSeconds();
            if (!onRelay.TryGetValue(post.Id, out var current) || current == wanted) continue;
            try { await relayFor(bubble.RelayUrl).SetExpiryAsync(bubble.MailboxId, post.Id, _identity.MessageSecret(post.Id), post.ExpiresAt, ct); }
            catch (HttpRequestException) { /* messages from older versions cannot be changed; the next load tries again */ }
        }
    }

    static bool HasLeft(List<BubbleMessage> byAuthor)
    {
        var left = byAuthor.Where(m => m.Kind == MessageKind.Left).Select(m => m.CreatedAt).DefaultIfEmpty().Max();
        return left != default && left >= byAuthor.Max(m => m.CreatedAt);
    }

    public async Task<IReadOnlyList<FeedPost>> LoadFeedAsync(BubbleMembership bubble, CancellationToken ct = default) =>
        (await LoadAsync(bubble, ct)).Posts;

    /// <summary>
    /// Shares a highlight with an emoji for the feeling. There is no daily limit. The post lasts as long as
    /// <paramref name="lifetime"/> says (default: what the bubble allows), but never longer than the bubble allows.
    /// </summary>
    public async Task PostAsync(BubbleMembership bubble, string? emoji, string? text, byte[]? image,
        PostLifetime? lifetime = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(text) && image is null)
            throw new ArgumentException("A post needs text or an image.");

        await SendAsync(bubble, new BubbleMessage
        {
            Kind = MessageKind.Post,
            AuthorKey = _identity.PublicKey,
            AuthorName = vault.DisplayName,
            CreatedAt = _time.GetUtcNow(),
            Lifetime = (lifetime ?? bubble.Lifetime) == PostLifetime.Story24h || bubble.Lifetime == PostLifetime.Story24h
                ? PostLifetime.Story24h : PostLifetime.Persistent,
            Emoji = string.IsNullOrWhiteSpace(emoji) ? null : emoji.Trim(),
            Text = string.IsNullOrWhiteSpace(text) ? null : text.Trim(),
            Image = image,
        }, ct);
    }

    /// <summary>Sets or takes back the heart on a post. Throws if the post has already expired.</summary>
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
            TargetExpiresAt = post.ExpiresAt,
        }, ct);
    }

    /// <summary>Comments on a post. The comment disappears together with the post.</summary>
    public async Task<Comment> CommentAsync(BubbleMembership bubble, FeedPost post, string text, CancellationToken ct = default)
    {
        text = text.Trim();
        if (text.Length == 0) throw new ArgumentException("A comment needs text.", nameof(text));
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
            TargetExpiresAt = post.ExpiresAt,
        }, ct);
        return new Comment(id, _identity.PublicKey, vault.DisplayName, createdAt, text);
    }

    void EnsureOpen(FeedPost post)
    {
        if (post.ExpiresAt is { } expires && expires <= _time.GetUtcNow())
            throw new InvalidOperationException("This highlight has already disappeared.");
    }

    /// <summary>
    /// The author makes one of their posts permanent or lets it disappear 24 hours from now.
    /// Permanent only works if the bubble allows it.
    /// </summary>
    public async Task SetPostLifetimeAsync(BubbleMembership bubble, FeedPost post, PostLifetime lifetime, CancellationToken ct = default)
    {
        if (!post.IsMine) throw new InvalidOperationException("Only the author can change a highlight.");
        EnsureOpen(post);
        if (lifetime == PostLifetime.Persistent && bubble.Lifetime == PostLifetime.Story24h)
            throw new InvalidOperationException("This bubble does not keep highlights permanently.");

        var now = _time.GetUtcNow();
        DateTimeOffset? expires = lifetime == PostLifetime.Story24h ? now.AddHours(24) : null;
        await SendAsync(bubble, new BubbleMessage
        {
            Kind = MessageKind.PostLifetimeChanged,
            AuthorKey = _identity.PublicKey,
            AuthorName = vault.DisplayName,
            CreatedAt = now,
            Lifetime = lifetime,
            TargetId = post.Id,
            TargetExpiresAt = expires,
        }, ct);
        try { await relayFor(bubble.RelayUrl).SetExpiryAsync(bubble.MailboxId, post.Id, _identity.MessageSecret(post.Id), expires, ct); }
        catch (HttpRequestException) { /* the next load brings the relay in line */ }
    }

    /// <summary>The author removes their highlight for everyone, including its hearts and comments.</summary>
    public async Task DeletePostAsync(BubbleMembership bubble, FeedPost post, CancellationToken ct = default)
    {
        if (!post.IsMine) throw new InvalidOperationException("Only the author can delete a highlight.");
        await relayFor(bubble.RelayUrl).DeleteMessageAsync(bubble.MailboxId, post.Id, _identity.MessageSecret(post.Id), ct);
    }

    /// <summary>Sets the longest lifetime posts in the bubble may have. Owner only; posts that last longer end 24 hours later.</summary>
    public async Task SetLifetimeAsync(BubbleMembership bubble, PostLifetime lifetime, CancellationToken ct = default)
    {
        if (bubble.OwnerKey is { } owner && owner != _identity.PublicKey)
            throw new InvalidOperationException("Only the owner of the bubble can change this.");
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

    /// <summary>Sends name, picture and bio to the bubble. After joining and after every profile change.</summary>
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

    /// <summary>Tells the bubble that I am leaving, so I disappear from the member list. The caller forgets the bubble locally.</summary>
    public Task LeaveAsync(BubbleMembership bubble, CancellationToken ct = default) =>
        SendAsync(bubble, new BubbleMessage
        {
            Kind = MessageKind.Left,
            AuthorKey = _identity.PublicKey,
            AuthorName = vault.DisplayName,
            CreatedAt = _time.GetUtcNow(),
            Lifetime = PostLifetime.Persistent,
        }, ct);

    public bool IsOwner(BubbleMembership bubble) => bubble.OwnerKey == _identity.PublicKey;

    /// <summary>
    /// Removes a member (owner only). The bubble moves to a fresh key that is sealed for each remaining
    /// member, so the removed member can neither read nor write anything new. Earlier posts stay under the old key.
    /// The remaining members pick up the new key the next time they load the bubble.
    /// </summary>
    public async Task RemoveMemberAsync(BubbleMembership bubble, string memberKey, CancellationToken ct = default)
    {
        if (!IsOwner(bubble)) throw new InvalidOperationException("Only the person who created the bubble can remove members.");
        if (memberKey == _identity.PublicKey) throw new ArgumentException("You cannot remove yourself – leave the bubble instead.", nameof(memberKey));

        var snapshot = await LoadAsync(bubble, ct);
        var recipients = snapshot.Members.Select(m => m.Key)
            .Where(k => k != memberKey)
            .Append(_identity.PublicKey)
            .Distinct()
            .ToList();

        var newKey = Crypto.NewKey();
        var wraps = recipients.ToDictionary(
            k => k,
            k => Convert.ToBase64String(Crypto.Seal(WrapKey(k), newKey, WrapAad(bubble, k))));

        await SendAsync(bubble, new BubbleMessage
        {
            Kind = MessageKind.KeyRotation,
            AuthorKey = _identity.PublicKey,
            AuthorName = vault.DisplayName,
            CreatedAt = _time.GetUtcNow(),
            Lifetime = PostLifetime.Persistent,
            Wraps = wraps,
        }, ct);

        bubble.Key = Base64Url.Encode(newKey);
        await PublishProfileAsync(bubble, ct);
    }

    byte[] WrapKey(string otherPublicKey) => Crypto.Derive(_identity.SharedSecretWith(otherPublicKey), "bubble/v1/rotation");

    static string WrapAad(BubbleMembership bubble, string recipient) => $"bubble/v1/rotation|{bubble.MailboxId}|{recipient}";

    byte[]? TryUnwrapKey(BubbleMembership bubble, string ownerKey, BubbleMessage rotation)
    {
        if (rotation.Wraps?.GetValueOrDefault(_identity.PublicKey) is not { } wrapped) return null;
        try
        {
            var key = Crypto.Open(WrapKey(ownerKey), Convert.FromBase64String(wrapped), WrapAad(bubble, _identity.PublicKey));
            return key.Length == Crypto.KeySize ? key : null;
        }
        catch (Exception e) when (e is CryptographicException or FormatException)
        {
            return null;
        }
    }

    async Task<string> SendAsync(BubbleMembership bubble, BubbleMessage message, CancellationToken ct)
    {
        var encrypted = MessageCodec.Encrypt(bubble, _identity, message);
        await relayFor(bubble.RelayUrl).PostMessageAsync(bubble.MailboxId, encrypted, ct);
        return encrypted.Id;
    }
}
