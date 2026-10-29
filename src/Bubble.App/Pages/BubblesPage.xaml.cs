using Bubble.App.Controls;
using Bubble.App.Services;
using Bubble.Core;

namespace Bubble.App.Pages;

public sealed record Face(string Name, byte[]? Image);

public sealed class BubbleTile(BubbleMembership bubble, BubbleSnapshot? snapshot, bool hasNew)
{
    public string MailboxId => bubble.MailboxId;
    public string Name => bubble.Name;
    public bool HasNew => hasNew;
    public IReadOnlyList<Face> Faces { get; } = (snapshot?.Members ?? []).Take(5).Select(m => new Face(m.Name, m.Avatar)).ToList();
    public string Meta => snapshot is null
        ? $"{Labels.Lifetime(bubble.Lifetime)}"
        : $"{Labels.People(snapshot.Members.Count)} · {Labels.Lifetime(bubble.Lifetime)}";
    readonly byte[]? _cover = snapshot?.Posts.FirstOrDefault(p => p.Message.Image is not null)?.Message.Image;
    public bool HasCover => _cover is not null;
    public ImageSource? Cover => _cover is { } bytes ? ImageSource.FromStream(() => new MemoryStream(bytes)) : null;
    public Brush Fallback { get; } = Gradients.For(bubble.MailboxId);
    public string Description => $"{Name}, {Meta}{(hasNew ? ", Neues" : "")}";
}

public partial class BubblesPage : ContentPage
{
    readonly AccountService _account;
    readonly FeedService _feed;
    string _sig = "";

    public BubblesPage(AccountService account, FeedService feed)
    {
        InitializeComponent();
        _account = account;
        _feed = feed;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (!await _account.LoadAsync()) return;
        Render();
        await _feed.RefreshAsync();
        Render();
    }

    async void OnRefreshing(object? sender, EventArgs e)
    {
        await _feed.RefreshAsync();
        Render();
        Refresher.IsRefreshing = false;
    }

    void Render()
    {
        var tiles = _account.Vault!.Bubbles
            .Select(b => new BubbleTile(b, _feed.SnapshotFor(b), _feed.StoriesOf(b).Any(s => !s.Post.IsMine && !_feed.IsSeen(s.Post.Id))))
            .ToList();
        // nur ersetzen, wenn sich etwas geändert hat (sonst laden alle Cover-Bilder neu und flackern)
        var sig = string.Join('|', tiles.Select(t => $"{t.MailboxId}:{t.Name}:{t.Meta}:{t.HasNew}:{t.Faces.Count}:{t.HasCover}"));
        if (sig == _sig) return;
        _sig = sig;
        List.ItemsSource = tiles;
    }

    async void OnTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is BubbleTile tile)
            await Shell.Current.GoToAsync($"{nameof(FeedPage)}?id={Uri.EscapeDataString(tile.MailboxId)}");
    }

    async void OnCreate(object? sender, EventArgs e) => await CreateBubbleFlowAsync(this, _account);

    async void OnJoin(object? sender, EventArgs e) => await Shell.Current.GoToAsync(nameof(JoinPage));

    /// <summary>Name und Lebensdauer abfragen, Bubble gründen und direkt zum Einladen wechseln.</summary>
    public static async Task CreateBubbleFlowAsync(Page page, AccountService account)
    {
        var name = await SheetPage.PromptAsync(page, "Neue Bubble", "Wie soll sie heißen?", "Weiter", "z. B. Familie, WG, Laufgruppe", maxLength: 40);
        if (string.IsNullOrWhiteSpace(name)) return;

        var choice = await SheetPage.ChooseAsync(page, "Wie lange sollen Highlights sichtbar sein?",
            "Das gilt für neue Highlights und lässt sich später ändern.", Labels.LifetimeOptions(PostLifetime.Story24h));
        if (choice is not { } index) return;

        var bubble = await account.CreateBubbleAsync(name, Labels.LifetimeAt(index));
        await Shell.Current.GoToAsync($"{nameof(InvitePage)}?id={Uri.EscapeDataString(bubble.MailboxId)}");
    }
}
