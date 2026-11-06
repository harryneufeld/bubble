using Bubble.App.Services;
using Bubble.App.Controls;
using Bubble.Core;

namespace Bubble.App.Pages;

/// <summary>A highlight full screen, in its own proportions, with heart bubble and comments below.</summary>
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
        var doubleTap = new TapGestureRecognizer { NumberOfTapsRequired = 2 };
        doubleTap.Tapped += OnDoubleTapped;
        Media.GestureRecognizers.Add(doubleTap);
        SizeChanged += (_, _) => FitMedia();
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
        Header.BindingContext = TopBar.BindingContext = _item;
        CommentList.ItemsSource = _item.Comments;
        ManageButton.IsVisible = post.Post.IsMine;
        FitMedia();
        return true;
    }

    /// <summary>
    /// As wide as the screen and exactly as tall as the photo's proportions say, so a 4:5 or a 9:16 highlight is never cut off.
    /// Only a photo even taller than the screen is fitted in. Text highlights get 4:5.
    /// </summary>
    void FitMedia()
    {
        if (_item is null || Width <= 0 || Height <= 0) return;
        var ratio = _item.Source.Post.Message.Image is { Length: > 0 } photo ? Thumbnails.RatioOf(photo) : 0.8;
        var height = Width / ratio;
        var limit = Height - 72;
        Media.HeightRequest = Math.Min(height, limit);
        Photo.Aspect = height > limit ? Aspect.AspectFit : Aspect.AspectFill;
    }

    async void OnClose(object? sender, EventArgs e) => await Shell.Current.GoToAsync("..");

    async void OnLikeClicked(object? sender, EventArgs e)
    {
        if (_item is null) return;
        _ = Pop(LikeButton);
        await _item.ToggleLikeAsync();
    }

    async void OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (_item is null) return;
        if (!_item.IsLiked)
        {
            _ = Pop(LikeButton);
            _ = _item.ToggleLikeAsync();
        }
        BigHeart.Scale = 0.4;
        await Task.WhenAll(BigHeart.FadeToAsync(1, 120), BigHeart.ScaleToAsync(1.1, 220, Easing.SpringOut));
        await Task.Delay(250);
        await Task.WhenAll(BigHeart.FadeToAsync(0, 220), BigHeart.ScaleToAsync(1.4, 220, Easing.CubicIn));
    }

    static async Task Pop(VisualElement view)
    {
        await view.ScaleToAsync(1.3, 90, Easing.CubicOut);
        await view.ScaleToAsync(1, 140, Easing.SpringOut);
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
