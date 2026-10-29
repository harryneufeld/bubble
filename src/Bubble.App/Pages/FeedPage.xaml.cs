using Bubble.App.Services;
using Bubble.Core;

namespace Bubble.App.Pages;

public sealed record MemberItem(string FullName, byte[]? Image, bool IsMe)
{
    public string Name => IsMe ? "Du" : FullName.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? FullName;
    public string Description => IsMe ? "Du" : FullName;
}

/// <summary>Eine Bubble: Cover, Mitglieder, Einladen und alle Highlights.</summary>
[QueryProperty(nameof(MailboxId), "id")]
public partial class FeedPage : ContentPage
{
    readonly AccountService _account;
    readonly FeedService _feed;
    BubbleMembership? _bubble;

    public string MailboxId { get; set; } = "";

    public FeedPage(AccountService account, FeedService feed)
    {
        InitializeComponent();
        _account = account;
        _feed = feed;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _bubble = _account.FindBubble(MailboxId);
        if (_bubble is null)
        {
            await Shell.Current.GoToAsync("..");
            return;
        }
        Render();
        await LoadAsync();
    }

    async void OnRefreshing(object? sender, EventArgs e) => await LoadAsync();

    async Task LoadAsync()
    {
        if (_bubble is null) return;
        try
        {
            await _feed.RefreshAsync(_bubble);
            Render();
        }
        catch (HttpRequestException ex)
        {
            await DisplayAlertAsync("Nicht geladen", Labels.ConnectionError(_bubble.RelayUrl, ex), "OK");
        }
        finally
        {
            Refresher.IsRefreshing = false;
        }
    }

    void Render()
    {
        var bubble = _bubble!;
        var snapshot = _feed.SnapshotFor(bubble);
        var posts = _feed.PostsOf(bubble);

        NameLabel.Text = bubble.Name;
        LifetimeLabel.Text = $"Neue Highlights: {Labels.Lifetime(bubble.Lifetime)}";
        PeopleLabel.Text = Labels.People(snapshot?.Members.Count ?? 1);
        var cover = posts.FirstOrDefault(p => p.Post.Message.Image is not null)?.Post.Message.Image;
        CoverImage.Source = cover is null ? null : ImageSource.FromStream(() => new MemoryStream(cover));
        CoverFrame.Background = Gradients.For(bubble.MailboxId);

        Members.ItemsSource = (snapshot?.Members ?? []).Select(m => new MemberItem(m.Name, m.Avatar, m.IsMe)).ToList();
        Posts.ItemsSource = posts.Select(p => new PostItem(p, showBubble: false)).ToList();

        var posted = _feed.HasPostedToday(bubble);
        ComposeButton.IsEnabled = !posted;
        ComposeButton.Text = posted ? "✓ Heute schon geteilt" : "Mein Highlight teilen";
    }

    async void OnCompose(object? sender, EventArgs e) =>
        await Shell.Current.GoToAsync($"{nameof(ComposePage)}?id={Uri.EscapeDataString(MailboxId)}");

    async void OnInvite(object? sender, EventArgs e) =>
        await Shell.Current.GoToAsync($"{nameof(InvitePage)}?id={Uri.EscapeDataString(MailboxId)}");

    async void OnChangeLifetime(object? sender, EventArgs e)
    {
        if (_bubble is null) return;
        var story = Labels.Lifetime(PostLifetime.Story24h);
        var persistent = Labels.Lifetime(PostLifetime.Persistent);
        var choice = await DisplayActionSheetAsync(
            "Gilt für neue Highlights. Bestehende behalten ihre Einstellung. Alle Mitglieder sehen die Änderung.",
            "Abbrechen", null, story, persistent);
        if (choice != story && choice != persistent) return;

        var lifetime = choice == story ? PostLifetime.Story24h : PostLifetime.Persistent;
        if (lifetime == _bubble.Lifetime) return;
        try
        {
            await _account.Client.SetLifetimeAsync(_bubble, lifetime);
            await _account.SaveAsync();
            await LoadAsync();
        }
        catch (HttpRequestException ex)
        {
            await DisplayAlertAsync("Nicht geändert", Labels.ConnectionError(_bubble.RelayUrl, ex), "OK");
        }
    }
}
