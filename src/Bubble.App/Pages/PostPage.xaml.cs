using Bubble.App.Services;
using Bubble.App.Controls;
using Bubble.Core;

namespace Bubble.App.Pages;

/// <summary>A highlight with heart bubble and comments.</summary>
[QueryProperty(nameof(MailboxId), "id")]
[QueryProperty(nameof(PostId), "post")]
[QueryProperty(nameof(ToComments), "comments")]
public partial class PostPage : ContentPage
{
    readonly AccountService _account;
    readonly FeedService _feed;
    BubbleMembership? _bubble;
    PostItem? _item;
    string _sig = "";

    public string MailboxId { get; set; } = "";
    public string PostId { get; set; } = "";

    /// <summary>"1": jump straight to the comments (from the story view).</summary>
    public string ToComments { get; set; } = "";
    bool _scrolled;

    public PostPage(AccountService account, FeedService feed)
    {
        InitializeComponent();
        _account = account;
        _feed = feed;
        _menu = new ToolbarItem { Text = "Manage" };
        _menu.Clicked += OnManage;
    }

    readonly ToolbarItem _menu;

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        _bubble = _account.FindBubble(MailboxId);
        if (_bubble is null || !Render())
        {
            await Shell.Current.GoToAsync("..");
            return;
        }
        GoToComments();
        await LoadAsync();
    }

    /// <summary>Scrolls past the card to the comments, or puts the cursor in the input field if there are none yet.</summary>
    void GoToComments()
    {
        if (ToComments != "1" || _scrolled || _item is null) return;
        _scrolled = true;
        if (_item.Comments.Count > 0) CommentList.ScrollTo(0, position: ScrollToPosition.Start, animate: true);
        else Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(400), () => CommentEntry.Focus());
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

    /// <summary>False if the post is gone.</summary>
    bool Render()
    {
        if (_feed.FindPost(_bubble!, PostId) is not { } post) return false;
        // Nothing new: do not rebuild the card, otherwise the photo reloads and flickers
        if (_item is not null && Sig(post) == _sig) return true;

        _item = new PostItem(post, _feed, showBubble: true, showFaces: true);
        _sig = Sig(post);
        _item.PropertyChanged += (_, _) => _sig = Sig(_item.Source); // own heart or comment: the state is already shown
        Card.BindingContext = _item;
        CommentList.ItemsSource = _item.Comments;
        Title = _item.Author == "You" ? "Your highlight" : $"Highlight from {_item.AuthorName}";
        ToolbarItems.Clear();
        if (post.Post.IsMine) ToolbarItems.Add(_menu);
        return true;
    }

    static string Sig(HomePost p) =>
        $"{p.Post.Id}:{p.Post.ExpiresAt?.ToUnixTimeSeconds()}:{p.Snapshot.LikersOf(p.Post.Id).Count}:{p.Snapshot.CommentsOf(p.Post.Id).Count}";

    /// <summary>The author keeps the highlight, lets it disappear, or deletes it.</summary>
    async void OnManage(object? sender, EventArgs e)
    {
        if (_bubble is null || _item is null || _feed.FindPost(_bubble, PostId) is not { } post) return;

        var options = new List<SheetOption>();
        var keep = post.Post.ExpiresAt is not null && _bubble.Lifetime == PostLifetime.Persistent;
        var expire = post.Post.ExpiresAt is null;
        if (keep) options.Add(new SheetOption("Remember", "Stays until you delete it.", "📌"));
        if (expire) options.Add(new SheetOption("Let it disappear", "Gone 24 hours from now, with its hearts and comments.", "⏳"));
        options.Add(new SheetOption("Delete", "Removes it for everyone, with its hearts and comments.", "🗑", Destructive: true));

        var choice = await SheetPage.ChooseAsync(this, "Your highlight", "", options);
        if (choice is not { } index) return;

        try
        {
            if (options[index].Destructive)
            {
                if (!await this.ShowAlertAsync("Delete highlight?", "It disappears for everyone in the bubble.", "Delete", "Cancel")) return;
                await _account.Client.DeletePostAsync(_bubble, post.Post);
                await _feed.RefreshAsync(_bubble);
                await Shell.Current.GoToAsync("..");
                return;
            }
            await _account.Client.SetPostLifetimeAsync(_bubble, post.Post, keep ? PostLifetime.Persistent : PostLifetime.Story24h);
            await _feed.RefreshAsync(_bubble);
            if (!Render()) await Shell.Current.GoToAsync("..");
        }
        catch (HttpRequestException ex)
        {
            await this.ShowAlertAsync("Not changed", Labels.ConnectionError(_bubble.RelayUrl, ex), "OK");
        }
        catch (InvalidOperationException ex)
        {
            await this.ShowAlertAsync("Not possible", ex.Message, "OK");
        }
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
            await this.ShowAlertAsync("Comment not sent", Labels.ConnectionError(_bubble!.RelayUrl, ex), "OK");
        }
        catch (InvalidOperationException ex)
        {
            await this.ShowAlertAsync("Not possible", ex.Message, "OK");
        }
        finally
        {
            SendButton.IsEnabled = true;
        }
    }
}
