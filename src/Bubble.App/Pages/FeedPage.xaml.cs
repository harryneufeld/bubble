using Bubble.App.Controls;
using Bubble.App.Services;
using Bubble.Core;

namespace Bubble.App.Pages;

public sealed record MemberItem(string Key, string FullName, byte[]? Image, bool IsMe)
{
    public string Name => IsMe ? "You" : FullName.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? FullName;
    public string Description => IsMe ? "You" : FullName;
}

/// <summary>One bubble: cover, members, inviting and all highlights.</summary>
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
            if (_feed.WasRemoved(_bubble))
            {
                _feed.TakeRemoved();
                await DisplayAlertAsync("Removed from bubble", $"The creator removed you from “{_bubble.Name}”.", "OK");
                await Shell.Current.GoToAsync("..");
                return;
            }
            Render();
        }
        catch (HttpRequestException ex)
        {
            await DisplayAlertAsync("Not loaded", Labels.ConnectionError(_bubble.RelayUrl, ex), "OK");
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
        LifetimeLabel.Text = $"New highlights: {Labels.Lifetime(bubble.Lifetime)}";
        PeopleLabel.Text = Labels.People(snapshot?.Members.Count ?? 1);
        var cover = posts.FirstOrDefault(p => p.Post.Message.Image is not null)?.Post.Message.Image;
        CoverImage.Source = cover is null ? null : ImageSource.FromStream(() => new MemoryStream(cover));
        CoverFrame.Background = Gradients.For(bubble.MailboxId);

        Members.ItemsSource = (snapshot?.Members ?? []).Select(m => new MemberItem(m.Key, m.Name, m.Avatar, m.IsMe)).ToList();
        Posts.ItemsSource = posts.Select(p => new PostItem(p, _feed, showBubble: false)).ToList();
    }

    async void OnPostTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is PostItem post)
            await Shell.Current.GoToAsync($"{nameof(PostPage)}?id={Uri.EscapeDataString(post.MailboxId)}&post={Uri.EscapeDataString(post.Id)}");
    }

    async void OnCompose(object? sender, EventArgs e) =>
        await Shell.Current.GoToAsync($"{nameof(ComposePage)}?id={Uri.EscapeDataString(MailboxId)}");

    async void OnInvite(object? sender, EventArgs e) =>
        await Shell.Current.GoToAsync($"{nameof(InvitePage)}?id={Uri.EscapeDataString(MailboxId)}");

    async void OnChangeLifetime(object? sender, EventArgs e)
    {
        if (_bubble is null) return;
        var choice = await SheetPage.ChooseAsync(this, "Lifetime",
            "Applies to new highlights. Existing ones keep their setting. All members see the change.",
            Labels.LifetimeOptions(_bubble.Lifetime));
        if (choice is not { } index) return;

        var lifetime = Labels.LifetimeAt(index);
        if (lifetime == _bubble.Lifetime) return;
        try
        {
            await _account.Client.SetLifetimeAsync(_bubble, lifetime);
            await _account.SaveAsync();
            await LoadAsync();
        }
        catch (HttpRequestException ex)
        {
            await DisplayAlertAsync("Not changed", Labels.ConnectionError(_bubble.RelayUrl, ex), "OK");
        }
    }

    async void OnMemberTapped(object? sender, TappedEventArgs e)
    {
        if (_bubble is null || e.Parameter is not MemberItem member || member.IsMe) return;
        if (!_account.Client.IsOwner(_bubble)) return; // only the creator can remove people

        var choice = await SheetPage.ChooseAsync(this, member.FullName,
            "Removing someone moves the bubble to a new key that only the remaining members get. Earlier highlights stay behind under the old key.",
            [new SheetOption("Remove from bubble", "They can no longer see or share anything new.", "🚪", Destructive: true)]);
        if (choice is null) return;

        try
        {
            await _account.Client.RemoveMemberAsync(_bubble, member.Key);
            await _account.SaveAsync();
            MailboxId = _bubble.MailboxId; // the new key means a new mailbox
            await LoadAsync();
        }
        catch (HttpRequestException ex)
        {
            await DisplayAlertAsync("Not removed", Labels.ConnectionError(_bubble.RelayUrl, ex), "OK");
        }
    }

    async void OnLeave(object? sender, EventArgs e)
    {
        if (_bubble is null) return;
        var owner = _account.Client.IsOwner(_bubble);
        var message = owner
            ? $"You created “{_bubble.Name}”. Nobody will be able to remove members after you leave. You can only come back with a new invitation."
            : $"You will no longer see “{_bubble.Name}”. You can only come back with a new invitation.";
        if (!await DisplayAlertAsync("Leave bubble?", message, "Leave", "Cancel")) return;

        await _account.LeaveAsync(_bubble);
        await Shell.Current.GoToAsync("..");
    }
}
