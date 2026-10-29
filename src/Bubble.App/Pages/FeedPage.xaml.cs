using Bubble.App.Services;
using Bubble.Core;

namespace Bubble.App.Pages;

public sealed class PostItem(FeedPost post)
{
    public string Author { get; } = post.IsMine ? $"{post.Message.AuthorName} (du)" : post.Message.AuthorName;
    public string Mood { get; } = Labels.Mood(post.Message.Mood);
    public bool IsTop { get; } = post.Message.Mood == Core.Mood.Top;
    public bool IsFlop { get; } = post.Message.Mood == Core.Mood.Flop;
    public string Time { get; } = Labels.Time(post.Message.CreatedAt);
    public string Remaining { get; } = Labels.Remaining(post.Message);
    public bool IsStory { get; } = post.Message.ExpiresAt is not null;
    public bool IsPersistent => !IsStory;
    public string? Text { get; } = post.Message.Text;
    public bool HasText => Text is not null;
    public bool HasImage { get; } = post.Message.Image is not null;
    public string ImageDescription => $"Foto von {post.Message.AuthorName}";
    public ImageSource? Image { get; } = post.Message.Image is { } bytes ? ImageSource.FromStream(() => new MemoryStream(bytes)) : null;
}

[QueryProperty(nameof(MailboxId), "id")]
public partial class FeedPage : ContentPage
{
    readonly AccountService _account;
    BubbleMembership? _bubble;

    public string MailboxId { get; set; } = "";

    public FeedPage(AccountService account)
    {
        InitializeComponent();
        _account = account;
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
        Title = _bubble.Name;
        await LoadAsync();
    }

    async void OnRefreshing(object? sender, EventArgs e) => await LoadAsync();

    async Task LoadAsync()
    {
        if (_bubble is null) return;
        Refresher.IsRefreshing = true;
        try
        {
            var client = _account.Client;
            var lifetimeBefore = _bubble.Lifetime;
            var feed = await client.LoadFeedAsync(_bubble);
            if (_bubble.Lifetime != lifetimeBefore) await _account.SaveAsync(); // von einem anderen Mitglied geändert
            Posts.ItemsSource = feed.Select(p => new PostItem(p)).ToList();
            var postedToday = client.HasPostedToday(feed);
            ComposeButton.IsEnabled = !postedToday;
            ComposeButton.Text = postedToday ? "✅ Heute schon geteilt" : "Mein Highlight teilen";
        }
        catch (HttpRequestException ex)
        {
            await DisplayAlertAsync("Feed nicht geladen", Labels.ConnectionError(_bubble.RelayUrl, ex), "OK");
        }
        finally
        {
            LifetimeLabel.Text = $"Neue Posts: {Labels.Lifetime(_bubble.Lifetime)}";
            Refresher.IsRefreshing = false;
        }
    }

    async void OnCompose(object? sender, EventArgs e) =>
        await Shell.Current.GoToAsync($"{nameof(ComposePage)}?id={Uri.EscapeDataString(MailboxId)}");

    async void OnInvite(object? sender, EventArgs e)
    {
        if (_bubble is null) return;
        var ok = await DisplayAlertAsync("Einladen",
            "Der Link enthält den Schlüssel der Bubble. Wer ihn hat, kann alles lesen. Teile ihn nur direkt mit Leuten, die du dabeihaben willst.",
            "Link teilen", "Abbrechen");
        if (!ok) return;
        await Share.Default.RequestAsync(new ShareTextRequest
        {
            Title = $"Einladung in „{_bubble.Name}“",
            Text = Invite.For(_bubble).ToLink(),
        });
    }

    async void OnChangeLifetime(object? sender, EventArgs e)
    {
        if (_bubble is null) return;
        var story = Labels.Lifetime(PostLifetime.Story24h);
        var persistent = Labels.Lifetime(PostLifetime.Persistent);
        var choice = await DisplayActionSheetAsync(
            "Gilt für neue Posts. Bestehende Posts behalten ihre Einstellung. Alle Mitglieder sehen die Änderung.",
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
