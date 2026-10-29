using Bubble.App.Services;
using Bubble.Core;

namespace Bubble.App.Pages;

/// <summary>Ein Highlight mit Herz-Blase und Kommentaren.</summary>
[QueryProperty(nameof(MailboxId), "id")]
[QueryProperty(nameof(PostId), "post")]
public partial class PostPage : ContentPage
{
    readonly AccountService _account;
    readonly FeedService _feed;
    BubbleMembership? _bubble;
    PostItem? _item;

    public string MailboxId { get; set; } = "";
    public string PostId { get; set; } = "";

    public PostPage(AccountService account, FeedService feed)
    {
        InitializeComponent();
        _account = account;
        _feed = feed;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _bubble = _account.FindBubble(MailboxId);
        if (_bubble is null || !Render())
        {
            await Shell.Current.GoToAsync("..");
            return;
        }
        await LoadAsync();
    }

    async void OnRefreshing(object? sender, EventArgs e) => await LoadAsync();

    async Task LoadAsync()
    {
        try
        {
            await _feed.RefreshAsync(_bubble!);
            if (!Render()) await Shell.Current.GoToAsync(".."); // inzwischen abgelaufen
        }
        catch (HttpRequestException)
        {
            // offline: wir zeigen, was wir haben
        }
        finally
        {
            Refresher.IsRefreshing = false;
        }
    }

    /// <summary>False, wenn der Post nicht mehr da ist.</summary>
    bool Render()
    {
        if (_feed.FindPost(_bubble!, PostId) is not { } post) return false;
        _item = new PostItem(post, _feed, showBubble: true);
        Card.BindingContext = _item;
        CommentList.ItemsSource = _item.Comments;
        Title = _item.Author == "Du" ? "Dein Highlight" : $"Highlight von {_item.AuthorName}";
        return true;
    }

    async void OnSend(object? sender, EventArgs e)
    {
        var text = CommentEntry.Text?.Trim();
        if (string.IsNullOrEmpty(text) || _item is null) return;

        SendButton.IsEnabled = false;
        try
        {
            await _feed.CommentAsync(_item.Source, text);
            CommentEntry.Text = "";
            _item.Changed();
            CommentList.ItemsSource = _item.Comments;
            if (_item.Comments.Count > 0) CommentList.ScrollTo(_item.Comments.Count - 1, position: ScrollToPosition.End, animate: true);
        }
        catch (HttpRequestException ex)
        {
            await DisplayAlertAsync("Kommentar nicht gesendet", Labels.ConnectionError(_bubble!.RelayUrl, ex), "OK");
        }
        catch (InvalidOperationException ex)
        {
            await DisplayAlertAsync("Nicht möglich", ex.Message, "OK");
        }
        finally
        {
            SendButton.IsEnabled = true;
        }
    }
}
