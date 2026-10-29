using Bubble.App.Controls;
using Bubble.App.Services;
using Bubble.Core;

namespace Bubble.App.Pages;

/// <summary>Ein Kreis in der Story-Leiste: „Du“ (teilen) oder eine Bubble mit Stories der letzten 24 h.</summary>
public sealed record StoryRing(string Title, string Name, byte[]? Image, AvatarRing Ring, bool ShowPlus, BubbleMembership? Bubble)
{
    public bool IsMe => Bubble is null;
    public Color TitleColor => Ring == AvatarRing.New || IsMe ? Colors.White : Color.FromArgb("#8F89A0");
    public string Description => IsMe
        ? (Ring == AvatarRing.None ? "Dein Highlight teilen" : "Deine Stories ansehen")
        : $"Stories aus {Title}";
}

public partial class HomePage : ContentPage
{
    readonly AccountService _account;
    readonly FeedService _feed;
    bool _loading;
    string _ringSig = "", _feedSig = "";

    public HomePage(AccountService account, FeedService feed)
    {
        InitializeComponent();
        _account = account;
        _feed = feed;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (!await _account.LoadAsync())
        {
            await Shell.Current.GoToAsync(nameof(OnboardingPage));
            return;
        }
        Render(); // sofort mit dem, was schon geladen ist
        await LoadAsync();
    }

    async void OnRefreshing(object? sender, EventArgs e) => await LoadAsync();

    async Task LoadAsync()
    {
        if (_loading) return;
        _loading = true;
        try
        {
            if (_account.Vault!.Bubbles.Count > 0) await _feed.RefreshAsync();
            Render();
        }
        finally
        {
            _loading = false;
            Refresher.IsRefreshing = false;
        }
    }

    void Render()
    {
        var vault = _account.Vault!;
        var bubbles = vault.Bubbles;

        // Stories: zuerst „Du“ (meine eigenen Posts der letzten 24 Stunden), dann Bubbles mit ungesehenen Stories, dann gesehene
        var rings = new List<StoryRing>();
        var mine = _feed.MyStories();
        var myRing = mine.Count == 0 ? AvatarRing.None : mine.Any(s => !_feed.IsSeen(s.Post.Id)) ? AvatarRing.New : AvatarRing.Seen;
        rings.Add(new StoryRing("Du", vault.DisplayName, vault.Avatar, myRing, true, null));
        rings.AddRange(bubbles
            .Select(b => (Bubble: b, Stories: _feed.StoriesOf(b)))
            .Where(x => x.Stories.Count > 0)
            .Select(x =>
            {
                var unseen = x.Stories.Any(s => !_feed.IsSeen(s.Post.Id));
                var cover = x.Stories.LastOrDefault(s => s.Post.Message.Image is not null)?.Post.Message.Image
                            ?? x.Stories[^1].Author?.Avatar;
                return new StoryRing(x.Bubble.Name, x.Bubble.Name, cover, unseen ? AvatarRing.New : AvatarRing.Seen, false, x.Bubble);
            })
            .OrderByDescending(r => r.Ring == AvatarRing.New));

        // Listen nur ersetzen, wenn sich etwas geändert hat: Beim Ersetzen laden alle Bilder neu und flackern.
        var ringSig = string.Join('|', rings.Select(r => $"{r.Title}:{r.Ring}:{r.Image?.Length}:{r.ShowPlus}"));
        if (ringSig != _ringSig)
        {
            _ringSig = ringSig;
            BindableLayout.SetItemsSource(StoriesRow, rings);
        }

        var posts = _feed.TimelinePosts();
        var feedSig = string.Join('|', posts.Select(p => PostSig(p))) + $"#{DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 60}";
        if (feedSig != _feedSig)
        {
            _feedSig = feedSig;
            Feed.ItemsSource = posts.Select(p => new PostItem(p, _feed, showBubble: true)).ToList();
        }
        FeedTitle.IsVisible = bubbles.Count > 0;

        if (bubbles.Count == 0)
        {
            EmptyTitle.Text = "Starte deine erste Bubble";
            EmptyText.Text = "Eine Bubble ist ein kleiner, privater Kreis – Familie, WG, beste Freunde. Gründe eine oder tritt per QR-Code bei.";
            EmptyAction.Text = "Bubble gründen";
            EmptySecondary.IsVisible = true;
        }
        else
        {
            EmptyTitle.Text = "Noch ganz still hier";
            EmptyText.Text = "Von deinen Leuten gibt es noch nichts Neues. Deine eigenen Highlights findest du oben im Kreis „Du“.";
            EmptyAction.Text = "Mein Highlight teilen";
            EmptySecondary.IsVisible = false;
        }

        OfflineBar.IsVisible = _feed.Unreachable.Count > 0;
        OfflineLabel.Text = _feed.Unreachable.Count switch
        {
            0 => "",
            1 => $"„{_feed.Unreachable[0].Name}“ ist gerade nicht erreichbar ({Labels.Host(_feed.Unreachable[0].RelayUrl)}).",
            var n => $"{n} Bubbles sind gerade nicht erreichbar.",
        };
    }

    static string PostSig(HomePost p) =>
        $"{p.Post.Id}:{p.Snapshot.LikersOf(p.Post.Id).Count}:{p.Snapshot.IsLikedByMe(p.Post.Id)}:{p.Snapshot.CommentsOf(p.Post.Id).Count}";

    async void OnStoryTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is not StoryRing ring) return;
        if (ring.IsMe)
        {
            // Eigene Stories ansehen; ohne welche gleich teilen
            if (ring.Ring == AvatarRing.None) await Compose();
            else await Shell.Current.GoToAsync($"{nameof(StoryPage)}?id={StoryPage.Mine}");
        }
        else await Shell.Current.GoToAsync($"{nameof(StoryPage)}?id={Uri.EscapeDataString(ring.Bubble!.MailboxId)}");
    }

    async void OnPlusTapped(object? sender, EventArgs e) => await Compose();

    async void OnPostTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is PostItem post)
            await Shell.Current.GoToAsync($"{nameof(PostPage)}?id={Uri.EscapeDataString(post.MailboxId)}&post={Uri.EscapeDataString(post.Id)}");
    }

    async void OnCompose(object? sender, EventArgs e) => await Compose();

    async Task Compose()
    {
        if (_account.Vault!.Bubbles.Count == 0)
        {
            await DisplayAlertAsync("Noch keine Bubble", "Gründe zuerst eine Bubble oder tritt einer bei.", "OK");
            return;
        }
        await Shell.Current.GoToAsync(nameof(ComposePage));
    }

    async void OnJoin(object? sender, EventArgs e) => await Shell.Current.GoToAsync(nameof(JoinPage));

    async void OnEmptyAction(object? sender, EventArgs e)
    {
        if (_account.Vault!.Bubbles.Count == 0) await BubblesPage.CreateBubbleFlowAsync(this, _account);
        else await Compose();
    }
}
