using Bubble.Core;

namespace Bubble.App.Services;

/// <summary>A post together with its bubble and the sender's profile.</summary>
public sealed record HomePost(BubbleMembership Bubble, FeedPost Post, Member? Author, BubbleSnapshot Snapshot);

/// <summary>
/// Loads all bubbles in parallel and keeps the result in memory until the next load,
/// so home, bubble page and story view show the same data. Remembers locally
/// which stories have been seen (message IDs only, no content).
/// </summary>
public sealed class FeedService(AccountService account)
{
    const string SeenKey = "bubble.seen";
    readonly Dictionary<string, BubbleSnapshot> _snapshots = [];
    HashSet<string>? _seen;

    /// <summary>Bubbles whose relay was not reachable on the last load.</summary>
    public List<BubbleMembership> Unreachable { get; } = [];

    /// <summary>Raised after posts were loaded, so a page that is already open can show them.</summary>
    public event Action? Refreshed;

    DateTime _lastResumeRefresh = DateTime.MinValue;
    bool _refreshing;

    /// <summary>Names of bubbles the owner removed me from since the last call to <see cref="TakeRemoved"/>.</summary>
    readonly List<string> _removed = [];

    public IReadOnlyList<string> TakeRemoved()
    {
        var names = _removed.ToList();
        _removed.Clear();
        return names;
    }

    /// <summary>Drops everything that was loaded, e.g. after logging out.</summary>
    public void Reset()
    {
        _snapshots.Clear();
        _removed.Clear();
        Unreachable.Clear();
    }

    public BubbleSnapshot? SnapshotFor(BubbleMembership bubble) => _snapshots.GetValueOrDefault(bubble.MailboxId);

    /// <summary>Shows what was loaded last time before the relay answers (and when it does not).</summary>
    public async Task LoadCachedAsync()
    {
        var vault = account.Vault;
        if (vault is null) return;
        foreach (var bubble in vault.Bubbles.ToList().Where(b => !_snapshots.ContainsKey(b.MailboxId)))
        {
            if (await account.Client.LoadCachedAsync(bubble) is { Removed: false } snapshot)
            {
                _snapshots[bubble.MailboxId] = snapshot;
                NotifiedPosts.MarkSeen(bubble.MailboxId, snapshot);
            }
        }
    }

    /// <summary>
    /// The app came back to the front, e.g. from tapping a notification: load again so the new post is already there.
    /// Never throws and does nothing if it just ran.
    /// </summary>
    public async Task RefreshOnResumeAsync()
    {
        if (account.Vault is not { Bubbles.Count: > 0 } || _refreshing || DateTime.UtcNow - _lastResumeRefresh < TimeSpan.FromSeconds(5)) return;
        _refreshing = true;
        _lastResumeRefresh = DateTime.UtcNow;
        try { await RefreshAsync(); }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or InvalidOperationException) { }
        finally { _refreshing = false; }
    }

    public async Task RefreshAsync()
    {
        var vault = account.Vault ?? throw new InvalidOperationException("No account.");
        var before = vault.Bubbles.ToDictionary(b => b, b => (b.Key, b.Lifetime));
        var results = await Task.WhenAll(vault.Bubbles.ToList().Select(async b =>
        {
            try { return (Bubble: b, Snapshot: (BubbleSnapshot?)await account.Client.LoadAsync(b)); }
            catch (HttpRequestException) { return (Bubble: b, Snapshot: (BubbleSnapshot?)null); }
        }));

        Unreachable.Clear();
        foreach (var (bubble, snapshot) in results)
        {
            if (snapshot is null) Unreachable.Add(bubble);
            else if (snapshot.Removed) await ForgetRemovedAsync(bubble);
            else
            {
                _snapshots[bubble.MailboxId] = snapshot;
                NotifiedPosts.MarkSeen(bubble.MailboxId, snapshot);
            }
        }
        // Another member changed the lifetime, or the owner moved the bubble to a new key
        // A bubble created while loading is not in `before`; creating it already saved the vault.
        if (vault.Bubbles.ToList().Any(b => before.TryGetValue(b, out var old) && old != (b.Key, b.Lifetime))) await account.SaveAsync();
        Refreshed?.Invoke();
    }

    public async Task RefreshAsync(BubbleMembership bubble)
    {
        var before = (bubble.Key, bubble.Lifetime);
        var snapshot = await account.Client.LoadAsync(bubble);
        if (snapshot.Removed)
        {
            await ForgetRemovedAsync(bubble);
            return;
        }
        _snapshots[bubble.MailboxId] = snapshot;
        NotifiedPosts.MarkSeen(bubble.MailboxId, snapshot);
        if (before != (bubble.Key, bubble.Lifetime)) await account.SaveAsync();
        Refreshed?.Invoke();
    }

    /// <summary>True if the bubble is gone because the owner removed me from it.</summary>
    public bool WasRemoved(BubbleMembership bubble) => account.FindBubble(bubble.MailboxId) is null;

    async Task ForgetRemovedAsync(BubbleMembership bubble)
    {
        _removed.Add(bubble.Name);
        _snapshots.Remove(bubble.MailboxId);
        await account.ForgetAsync(bubble);
    }

    /// <summary>Alle Posts aller Bubbles, neueste zuerst.</summary>
    public IReadOnlyList<HomePost> AllPosts() => (account.Vault?.Bubbles ?? [])
        .SelectMany(b => SnapshotFor(b) is { } s ? s.Posts.Select(p => new HomePost(b, p, s.MemberFor(p.Message.AuthorKey), s)) : [])
        .OrderByDescending(p => p.Post.Message.CreatedAt)
        .ToList();

    public IReadOnlyList<HomePost> PostsOf(BubbleMembership bubble) => SnapshotFor(bubble) is { } s
        ? s.Posts.Select(p => new HomePost(bubble, p, s.MemberFor(p.Message.AuthorKey), s)).ToList()
        : [];

    /// <summary>What others have shared, newest first. Own posts only appear in the "You" circle, not in the timeline.</summary>
    public IReadOnlyList<HomePost> TimelinePosts() => AllPosts().Where(p => !p.Post.IsMine).ToList();

    /// <summary>Stories of others from a bubble (posts of the last 24 hours), oldest first.</summary>
    public IReadOnlyList<HomePost> StoriesOf(BubbleMembership bubble) => LastDay(PostsOf(bubble).Where(p => !p.Post.IsMine));

    /// <summary>My own stories from all bubbles, oldest first.</summary>
    public IReadOnlyList<HomePost> MyStories() => LastDay(AllPosts().Where(p => p.Post.IsMine));

    static IReadOnlyList<HomePost> LastDay(IEnumerable<HomePost> posts)
    {
        var since = DateTimeOffset.UtcNow.AddHours(-24);
        return posts.Where(p => p.Post.Message.CreatedAt > since).OrderBy(p => p.Post.Message.CreatedAt).ToList();
    }

    public HomePost? FindPost(BubbleMembership bubble, string postId) =>
        PostsOf(bubble).FirstOrDefault(p => p.Post.Id == postId);

    /// <summary>
    /// Sets or takes back the heart. The UI reacts immediately; if sending fails,
    /// it is rolled back and the exception is passed on.
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

    /// <summary>Comments and shows the comment immediately.</summary>
    public async Task CommentAsync(HomePost item, string text)
    {
        var comment = await account.Client.CommentAsync(item.Bubble, item.Post, text);
        item.Snapshot.AddComment(item.Post.Id, comment);
    }

    public bool IsSeen(string postId) => Seen.Contains(postId);

    public void MarkSeen(string postId)
    {
        if (!Seen.Add(postId)) return;
        // Limit the size: story IDs are worthless after 24 h anyway
        Preferences.Default.Set(SeenKey, string.Join(',', Seen.TakeLast(500)));
    }

    HashSet<string> Seen => _seen ??= [.. Preferences.Default.Get(SeenKey, "").Split(',', StringSplitOptions.RemoveEmptyEntries)];
}
