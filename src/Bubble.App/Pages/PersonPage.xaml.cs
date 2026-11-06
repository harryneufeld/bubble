using Bubble.App.Services;
using Bubble.Core;

namespace Bubble.App.Pages;

/// <summary>A bubble you share with someone, on their profile.</summary>
public sealed record SharedBubble(string MailboxId, string Name)
{
    public string Description => $"Bubble {Name}";
}

/// <summary>
/// Someone's profile as you know them from your bubbles: what they last told about themselves, the bubbles you share,
/// and their highlights that are still there. Only what your bubbles already hold – nothing extra is loaded.
/// </summary>
[QueryProperty(nameof(Key), "key")]
public partial class PersonPage : ContentPage
{
    readonly AccountService _account;
    readonly FeedService _feed;
    BubbleMembership? _storyBubble;

    /// <summary>The person's public key, as it signs their messages.</summary>
    public string Key { get; set; } = "";

    public PersonPage(AccountService account, FeedService feed)
    {
        InitializeComponent();
        _account = account;
        _feed = feed;
    }

    /// <summary>Opens someone's profile; your own is the Profile tab. From a modal page pass the "../" prefix.</summary>
    public static Task OpenAsync(string authorKey, bool isMe, string prefix = "") => isMe
        ? Shell.Current.GoToAsync("//profile")
        : Shell.Current.GoToAsync($"{prefix}{nameof(PersonPage)}?key={Uri.EscapeDataString(authorKey)}");

    protected override void OnAppearing()
    {
        base.OnAppearing();
        var bubbles = (_account.Vault?.Bubbles ?? []).ToList();
        var shared = bubbles
            .Select(b => (Bubble: b, Member: _feed.SnapshotFor(b)?.MemberFor(Key)))
            .Where(x => x.Member is not null)
            .ToList();
        var posts = _feed.AllPosts().Where(p => p.Post.Message.AuthorKey == Key).ToList();

        // The profile they sent most recently; without one, the name from their newest highlight
        var member = shared.Select(x => x.Member!).FirstOrDefault(m => m.Bio is not null || m.Avatar is not null) ?? shared.FirstOrDefault().Member;
        var name = member?.Name ?? posts.FirstOrDefault()?.Post.Message.AuthorName ?? "Someone";
        Title = name;
        NameLabel.Text = name;
        BioLabel.Text = member?.Bio ?? "";
        BioLabel.IsVisible = member?.Bio is { Length: > 0 };
        Avatar.Name = name;
        Avatar.Image = member?.Avatar;

        // A ring and a way into their stories if they shared something in the last 24 hours
        _storyBubble = shared.Select(x => x.Bubble).FirstOrDefault(b => _feed.StoriesOf(b).Any(s => s.Post.Message.AuthorKey == Key));
        var unseen = _storyBubble is not null && _feed.StoriesOf(_storyBubble).Any(s => s.Post.Message.AuthorKey == Key && !_feed.IsSeen(s.Post.Id));
        Avatar.Ring = _storyBubble is null ? Controls.AvatarRing.None : unseen ? Controls.AvatarRing.New : Controls.AvatarRing.Seen;
        StoriesButton.IsVisible = _storyBubble is not null;

        BindableLayout.SetItemsSource(Bubbles, shared.Select(x => new SharedBubble(x.Bubble.MailboxId, x.Bubble.Name)).ToList());
        Months.ItemsSource = Timeline.ByMonth(posts, p => p.Post.Message.CreatedAt, DateTimeOffset.Now)
            .Select(m => new MonthItems(Labels.Month(m.Year, m.Month), m.Items.Select(p => new PostItem(p, _feed, showBubble: true)).ToList()))
            .ToList();
        EmptyLabel.Text = $"Nothing from {name.Split(' ')[0]} that is still there.";
    }

    async void OnStories(object? sender, EventArgs e)
    {
        if (_storyBubble is { } bubble) await Shell.Current.GoToAsync($"{nameof(StoryPage)}?id={Uri.EscapeDataString(bubble.MailboxId)}");
    }

    async void OnBubbleTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is SharedBubble bubble) await Shell.Current.GoToAsync($"{nameof(FeedPage)}?id={Uri.EscapeDataString(bubble.MailboxId)}");
    }

    async void OnPostTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is PostItem post)
            await Shell.Current.GoToAsync($"{nameof(PostPage)}?id={Uri.EscapeDataString(post.MailboxId)}&post={Uri.EscapeDataString(post.Id)}");
    }
}
