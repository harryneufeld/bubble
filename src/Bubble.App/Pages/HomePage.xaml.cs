using Bubble.App.Controls;
using Bubble.App.Services;
using Bubble.Core;

namespace Bubble.App.Pages;

/// <summary>Ein Kreis in der Story-Leiste: „Du“ (teilen) oder eine Bubble mit Stories der letzten 24 h.</summary>
public sealed record StoryRing(string Title, string Name, byte[]? Image, AvatarRing Ring, bool ShowPlus, BubbleMembership? Bubble)
{
    public Color TitleColor => Ring == AvatarRing.New || Bubble is null ? Colors.White : Color.FromArgb("#8F89A0");
    public string Description => Bubble is null ? "Dein Highlight teilen" : $"Stories aus {Title}";
}

public partial class HomePage : ContentPage
{
    readonly AccountService _account;
    readonly FeedService _feed;
    bool _loading;

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

        // Stories: zuerst „Du“, dann Bubbles mit ungesehenen Stories, dann gesehene
        var rings = new List<StoryRing>();
        rings.Add(new StoryRing("Du", vault.DisplayName, vault.Avatar, AvatarRing.None, true, null));
        rings.AddRange(bubbles
            .Select(b => (Bubble: b, Stories: _feed.StoriesOf(b)))
            .Where(x => x.Stories.Count > 0)
            .Select(x =>
            {
                var unseen = x.Stories.Any(s => !s.Post.IsMine && !_feed.IsSeen(s.Post.Id));
                var cover = x.Stories.LastOrDefault(s => s.Post.Message.Image is not null)?.Post.Message.Image
                            ?? x.Stories[^1].Author?.Avatar;
                return new StoryRing(x.Bubble.Name, x.Bubble.Name, cover, unseen ? AvatarRing.New : AvatarRing.Seen, false, x.Bubble);
            })
            .OrderByDescending(r => r.Ring == AvatarRing.New));
        Stories.ItemsSource = rings;

        Feed.ItemsSource = _feed.AllPosts().Select(p => new PostItem(p, _feed, showBubble: true)).ToList();
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
            EmptyText.Text = "Heute hat noch niemand etwas geteilt. Wie war dein Tag?";
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

    async void OnStoryTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is not StoryRing ring) return;
        if (ring.Bubble is null) await Compose();
        else await Shell.Current.GoToAsync($"{nameof(StoryPage)}?id={Uri.EscapeDataString(ring.Bubble.MailboxId)}");
    }

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
