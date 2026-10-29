using Bubble.Core;

namespace Bubble.App.Services;

/// <summary>Ein Post zusammen mit seiner Bubble und dem Profil des Absenders.</summary>
public sealed record HomePost(BubbleMembership Bubble, FeedPost Post, Member? Author);

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
        .SelectMany(b => SnapshotFor(b) is { } s ? s.Posts.Select(p => new HomePost(b, p, s.MemberFor(p.Message.AuthorKey))) : [])
        .OrderByDescending(p => p.Post.Message.CreatedAt)
        .ToList();

    public IReadOnlyList<HomePost> PostsOf(BubbleMembership bubble) => SnapshotFor(bubble) is { } s
        ? s.Posts.Select(p => new HomePost(bubble, p, s.MemberFor(p.Message.AuthorKey))).ToList()
        : [];

    /// <summary>Posts der letzten 24 Stunden – die „Stories“ einer Bubble, älteste zuerst.</summary>
    public IReadOnlyList<HomePost> StoriesOf(BubbleMembership bubble)
    {
        var since = DateTimeOffset.UtcNow.AddHours(-24);
        return PostsOf(bubble).Where(p => p.Post.Message.CreatedAt > since).OrderBy(p => p.Post.Message.CreatedAt).ToList();
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
