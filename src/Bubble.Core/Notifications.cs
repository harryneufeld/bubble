namespace Bubble.Core;

public enum NotificationKind { Post, Heart, Comment }

/// <summary>Something worth a notification. <see cref="Key"/> identifies it so it is announced only once.</summary>
public sealed record NotificationEvent(NotificationKind Kind, string Key, string Text, DateTimeOffset At);

/// <summary>
/// Decides what a wake announces: new posts by others, and hearts and comments by others on your own posts.
/// What was announced (or seen) is remembered as keys: the raw message ID for a post, <c>l:postId:memberKey</c>
/// for a heart and <c>c:commentId</c> for a comment.
/// </summary>
public static class PostNotifications
{
    /// <summary>Events that are not in <paramref name="known"/> yet, oldest first.</summary>
    public static List<NotificationEvent> NewEvents(BubbleSnapshot snapshot, IReadOnlySet<string> known)
    {
        var events = new List<NotificationEvent>();
        foreach (var post in snapshot.Posts)
        {
            if (!post.IsMine)
            {
                if (!known.Contains(post.Id))
                    events.Add(new NotificationEvent(NotificationKind.Post, post.Id, Describe(post), post.Message.CreatedAt));
                continue;
            }

            foreach (var liker in LikersOf(snapshot, post))
            {
                var key = HeartKey(post.Id, liker.Key);
                if (!known.Contains(key))
                    events.Add(new NotificationEvent(NotificationKind.Heart, key, $"{liker.Name} ❤️ your highlight", post.Message.CreatedAt));
            }

            foreach (var comment in CommentsOn(snapshot, post))
            {
                var key = CommentKey(comment.Id);
                if (!known.Contains(key))
                    events.Add(new NotificationEvent(NotificationKind.Comment, key, Shorten($"{comment.AuthorName}: {comment.Text}"), comment.CreatedAt));
            }
        }
        return [.. events.OrderBy(e => e.At)];
    }

    /// <summary>Everything the snapshot currently contains, whatever the notification settings: so switching a type on later announces only what is new from then on.</summary>
    public static HashSet<string> KnownKeys(BubbleSnapshot snapshot)
    {
        var keys = new HashSet<string>();
        foreach (var post in snapshot.Posts)
        {
            keys.Add(post.Id);
            if (!post.IsMine) continue;
            foreach (var liker in LikersOf(snapshot, post)) keys.Add(HeartKey(post.Id, liker.Key));
            foreach (var comment in CommentsOn(snapshot, post)) keys.Add(CommentKey(comment.Id));
        }
        return keys;
    }

    /// <summary>One line for the notification: the event itself, or a count per type, e.g. "2 new highlights, 1 heart".</summary>
    public static string Summarize(IReadOnlyList<NotificationEvent> events)
    {
        if (events.Count == 1) return events[0].Text;
        var parts = new List<string>();
        Add(NotificationKind.Post, "new highlight", "new highlights");
        Add(NotificationKind.Heart, "heart", "hearts");
        Add(NotificationKind.Comment, "comment", "comments");
        return string.Join(", ", parts);

        void Add(NotificationKind kind, string one, string many)
        {
            var n = events.Count(e => e.Kind == kind);
            if (n > 0) parts.Add(n == 1 ? $"1 {one}" : $"{n} {many}");
        }
    }

    /// <summary>A line for a post, e.g. "Anna posted a highlight 🥳". The text of the post stays out: it shows once the app is open.</summary>
    public static string Describe(FeedPost post) =>
        Shorten(string.Join(' ', new[] { post.Message.AuthorName, "posted a highlight", post.Message.Emoji }.Where(s => !string.IsNullOrWhiteSpace(s))));

    static IEnumerable<Member> LikersOf(BubbleSnapshot snapshot, FeedPost post) =>
        snapshot.LikersOf(post.Id).Where(m => !m.IsMe && m.Key != snapshot.MyKey);

    static IEnumerable<Comment> CommentsOn(BubbleSnapshot snapshot, FeedPost post) =>
        snapshot.CommentsOf(post.Id).Where(c => c.AuthorKey != snapshot.MyKey);

    static string HeartKey(string postId, string memberKey) => $"l:{postId}:{memberKey}";

    static string CommentKey(string commentId) => $"c:{commentId}";

    static string Shorten(string line)
    {
        line = line.ReplaceLineEndings(" ");
        return line.Length <= 100 ? line : line[..99].TrimEnd() + "…";
    }
}
