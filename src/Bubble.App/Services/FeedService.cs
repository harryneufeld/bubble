using Bubble.Core;

namespace Bubble.App.Services;

/// <summary>Ein Post zusammen mit seiner Bubble und dem Profil des Absenders.</summary>
public sealed record HomePost(BubbleMembership Bubble, FeedPost Post, Member? Author, BubbleSnapshot Snapshot);

/// <summary>
/// Lädt alle Bubbles parallel und hält das Ergebnis bis zum nächsten Laden im Speicher,
/// damit Home, Bubble-Seite und Story-Ansicht dieselben Daten zeigen. Merkt sich lokal,
/// welche Stories schon angesehen wurden (nur Nachrichten-IDs, keine Inhalte).
/// </summary>
public sealed class FeedService(AccountService account)
{
    const string SeenKey = "bubble.seen";
    readonly Dictionary<string, BubbleSnapshot> _snapshots = [];
    HashSet<string>? _seen;

    /// <summary>Bubbles, deren Relay beim letzten Laden nicht erreichbar war.</summary>
    public List<BubbleMembership> Unreachable { get; } = [];

    public BubbleSnapshot? SnapshotFor(BubbleMembership bubble) => _snapshots.GetValueOrDefault(bubble.MailboxId);

    public async Task RefreshAsync()
    {
        var vault = account.Vault ?? throw new InvalidOperationException("Kein Account.");
        var lifetimes = vault.Bubbles.ToDictionary(b => b.MailboxId, b => b.Lifetime);
        var results = await Task.WhenAll(vault.Bubbles.Select(async b =>
        {
            try { return (Bubble: b, Snapshot: (BubbleSnapshot?)await account.Client.LoadAsync(b)); }
            catch (HttpRequestException) { return (Bubble: b, Snapshot: (BubbleSnapshot?)null); }
        }));

        Unreachable.Clear();
        foreach (var (bubble, snapshot) in results)
        {
            if (snapshot is null) Unreachable.Add(bubble);
            else _snapshots[bubble.MailboxId] = snapshot;
        }
        // Ein anderes Mitglied hat die Lebensdauer geändert
        if (vault.Bubbles.Any(b => lifetimes[b.MailboxId] != b.Lifetime)) await account.SaveAsync();
    }

    public async Task RefreshAsync(BubbleMembership bubble)
    {
        var before = bubble.Lifetime;
        _snapshots[bubble.MailboxId] = await account.Client.LoadAsync(bubble);
        if (bubble.Lifetime != before) await account.SaveAsync();
    }

    /// <summary>Alle Posts aller Bubbles, neueste zuerst.</summary>
    public IReadOnlyList<HomePost> AllPosts() => (account.Vault?.Bubbles ?? [])
        .SelectMany(b => SnapshotFor(b) is { } s ? s.Posts.Select(p => new HomePost(b, p, s.MemberFor(p.Message.AuthorKey), s)) : [])
        .OrderByDescending(p => p.Post.Message.CreatedAt)
        .ToList();

    public IReadOnlyList<HomePost> PostsOf(BubbleMembership bubble) => SnapshotFor(bubble) is { } s
        ? s.Posts.Select(p => new HomePost(bubble, p, s.MemberFor(p.Message.AuthorKey), s)).ToList()
        : [];

    /// <summary>Was andere geteilt haben, neueste zuerst. Eigene Posts stehen nur im „Du“-Kreis, nicht in der Timeline.</summary>
    public IReadOnlyList<HomePost> TimelinePosts() => AllPosts().Where(p => !p.Post.IsMine).ToList();

    /// <summary>Stories anderer aus einer Bubble (Posts der letzten 24 Stunden), älteste zuerst.</summary>
    public IReadOnlyList<HomePost> StoriesOf(BubbleMembership bubble) => LastDay(PostsOf(bubble).Where(p => !p.Post.IsMine));

    /// <summary>Meine eigenen Stories aus allen Bubbles, älteste zuerst.</summary>
    public IReadOnlyList<HomePost> MyStories() => LastDay(AllPosts().Where(p => p.Post.IsMine));

    static IReadOnlyList<HomePost> LastDay(IEnumerable<HomePost> posts)
    {
        var since = DateTimeOffset.UtcNow.AddHours(-24);
        return posts.Where(p => p.Post.Message.CreatedAt > since).OrderBy(p => p.Post.Message.CreatedAt).ToList();
    }

    public HomePost? FindPost(BubbleMembership bubble, string postId) =>
        PostsOf(bubble).FirstOrDefault(p => p.Post.Id == postId);

    /// <summary>
    /// Setzt oder nimmt das Herz zurück. Die Oberfläche reagiert sofort; schlägt das Senden fehl,
    /// wird zurückgesetzt und die Ausnahme weitergereicht.
    /// </summary>
    public async Task ToggleLikeAsync(HomePost item)
    {
        var id = item.Post.Id;
        var liked = !item.Snapshot.IsLikedByMe(id);
        item.Snapshot.SetLikeByMe(id, liked);
        try { await account.Client.LikeAsync(item.Bubble, item.Post, liked); }
        catch
        {
            item.Snapshot.SetLikeByMe(id, !liked);
            throw;
        }
    }

    /// <summary>Kommentiert und zeigt den Kommentar sofort an.</summary>
    public async Task CommentAsync(HomePost item, string text)
    {
        var comment = await account.Client.CommentAsync(item.Bubble, item.Post, text);
        item.Snapshot.AddComment(item.Post.Id, comment);
    }

    public bool IsSeen(string postId) => Seen.Contains(postId);

    public void MarkSeen(string postId)
    {
        if (!Seen.Add(postId)) return;
        // Begrenzen: Story-IDs sind nach 24 h ohnehin wertlos
        Preferences.Default.Set(SeenKey, string.Join(',', Seen.TakeLast(500)));
    }

    HashSet<string> Seen => _seen ??= [.. Preferences.Default.Get(SeenKey, "").Split(',', StringSplitOptions.RemoveEmptyEntries)];
}
