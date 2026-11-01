using Bubble.Core;

namespace Bubble.Tests;

public class NotificationTests
{
    static readonly DateTimeOffset Now = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);

    static BubbleMessage Message(string author, string? text, DateTimeOffset at) => new()
    {
        Kind = MessageKind.Post, AuthorKey = author, AuthorName = author, CreatedAt = at,
        Lifetime = PostLifetime.Story24h, Emoji = "🥳", Text = text,
    };

    static readonly Member[] Members =
    [
        new("me", "Me", null, null, true),
        new("anna", "Anna", null, null, false),
        new("ben", "Ben", null, null, false),
    ];

    static Comment CommentBy(string id, string key, string text) =>
        new(id, key, Members.First(m => m.Key == key).Name, Now, text);

    static IReadOnlySet<string> Nothing => new HashSet<string>();

    [Fact]
    public void NewPostsByOthers_AreAnnounced_OldAndOwnAreNot()
    {
        var snapshot = new BubbleSnapshot(
            [
                new FeedPost("new-2", Message("Ben", "Second\nline", Now), false),
                new FeedPost("new-1", Message("Anna", "First", Now.AddMinutes(-5)), false),
                new FeedPost("seen", Message("Anna", "Old", Now.AddHours(-1)), false),
                new FeedPost("mine", Message("Me", "Mine", Now), true),
            ],
            Members, "me", [], []);

        var events = PostNotifications.NewEvents(snapshot, new HashSet<string> { "seen" });

        Assert.Equal(["new-1", "new-2"], events.Select(e => e.Key));
        Assert.All(events, e => Assert.Equal(NotificationKind.Post, e.Kind));
        Assert.Equal("Anna posted a highlight ✨", events[0].Text);
        Assert.Equal("Ben posted a highlight ✨", events[1].Text);
    }

    [Fact]
    public void HeartsOnMyPosts_AreAnnouncedOnce_OwnHeartsAndOthersPostsAreNot()
    {
        var snapshot = new BubbleSnapshot(
            [
                new FeedPost("mine", Message("Me", "Mine", Now), true),
                new FeedPost("theirs", Message("Anna", "Theirs", Now), false),
            ],
            Members, "me",
            new() { ["mine"] = ["anna", "me"], ["theirs"] = ["ben"] },
            []);

        var events = PostNotifications.NewEvents(snapshot, new HashSet<string> { "theirs" });

        var heart = Assert.Single(events);
        Assert.Equal(NotificationKind.Heart, heart.Kind);
        Assert.Equal("Anna ❤️ your highlight", heart.Text);

        Assert.Empty(PostNotifications.NewEvents(snapshot, new HashSet<string> { "theirs", heart.Key }));
    }

    [Fact]
    public void CommentsOnMyPosts_AreAnnounced_OwnCommentsAndOthersPostsAreNot()
    {
        var snapshot = new BubbleSnapshot(
            [
                new FeedPost("mine", Message("Me", "Mine", Now), true),
                new FeedPost("theirs", Message("Anna", "Theirs", Now), false),
            ],
            Members, "me", [],
            new()
            {
                ["mine"] = [CommentBy("c1", "ben", "Nice one!"), CommentBy("c2", "me", "Thanks")],
                ["theirs"] = [CommentBy("c3", "ben", "Hello")],
            });

        var events = PostNotifications.NewEvents(snapshot, new HashSet<string> { "theirs" });

        var comment = Assert.Single(events);
        Assert.Equal(NotificationKind.Comment, comment.Kind);
        Assert.Equal("Ben: Nice one!", comment.Text);
    }

    [Fact]
    public void KnownKeys_CoverEverything_SoNothingIsAnnouncedTwice()
    {
        var snapshot = new BubbleSnapshot(
            [
                new FeedPost("mine", Message("Me", "Mine", Now), true),
                new FeedPost("theirs", Message("Anna", "Theirs", Now), false),
            ],
            Members, "me",
            new() { ["mine"] = ["anna"] },
            new() { ["mine"] = [CommentBy("c1", "ben", "Nice")] });

        Assert.Equal(3, PostNotifications.NewEvents(snapshot, Nothing).Count);

        Assert.Empty(PostNotifications.NewEvents(snapshot, PostNotifications.KnownKeys(snapshot)));
    }

    [Fact]
    public void NewHeartOnAKnownPost_IsAnnounced_ButTheOldOnesAreNot()
    {
        BubbleSnapshot Snapshot(params string[] likers) => new(
            [new FeedPost("mine", Message("Me", "Mine", Now), true)],
            Members, "me", new() { ["mine"] = [.. likers] }, []);

        var known = PostNotifications.KnownKeys(Snapshot("anna"));
        var events = PostNotifications.NewEvents(Snapshot("anna", "ben"), known);

        Assert.Equal("Ben ❤️ your highlight", Assert.Single(events).Text);
    }

    [Fact]
    public void Summarize_NamesASingleEvent_AndCountsSeveral()
    {
        NotificationEvent Event(NotificationKind kind, string text) => new(kind, text, text, Now);

        Assert.Equal("Anna ❤️ your highlight", PostNotifications.Summarize([Event(NotificationKind.Heart, "Anna ❤️ your highlight")]));
        Assert.Equal("2 new highlights", PostNotifications.Summarize([Event(NotificationKind.Post, "a"), Event(NotificationKind.Post, "b")]));
        Assert.Equal("1 new highlight, 2 hearts, 1 comment", PostNotifications.Summarize(
        [
            Event(NotificationKind.Comment, "c"), Event(NotificationKind.Heart, "h1"), Event(NotificationKind.Post, "p"), Event(NotificationKind.Heart, "h2"),
        ]));
    }

    [Fact]
    public void LongComments_AreShortened_AndPostTextStaysOut()
    {
        var snapshot = new BubbleSnapshot(
            [new FeedPost("x", Message("Me", "Secret text", Now), true)],
            Members, "me", [], new() { ["x"] = [CommentBy("c", "anna", new string('w', 500))] });

        var line = Assert.Single(PostNotifications.NewEvents(snapshot, Nothing)).Text;

        Assert.True(line.Length <= 100);
        Assert.EndsWith("…", line);

        var post = new BubbleSnapshot([new FeedPost("y", Message("Anna", "Secret text", Now), false)], Members, "me", [], []);
        Assert.DoesNotContain("Secret", Assert.Single(PostNotifications.NewEvents(post, Nothing)).Text);
    }
}
