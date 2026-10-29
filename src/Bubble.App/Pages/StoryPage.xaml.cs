using Bubble.App.Services;

namespace Bubble.App.Pages;

[QueryProperty(nameof(MailboxId), "id")]
public partial class StoryPage : ContentPage
{
    static readonly TimeSpan PerStory = TimeSpan.FromSeconds(6);

    readonly AccountService _account;
    readonly FeedService _feed;
    readonly IDispatcherTimer _timer;
    IReadOnlyList<HomePost> _stories = [];
    List<BoxView> _bars = [];
    int _index;
    DateTime _shownAt;
    PostItem? _current;

    /// <summary>Als Bubble-ID übergeben: alle meine eigenen Stories aus allen Bubbles.</summary>
    public const string Mine = "me";

    public string MailboxId { get; set; } = "";

    public StoryPage(AccountService account, FeedService feed)
    {
        InitializeComponent();
        _account = account;
        _feed = feed;
        _timer = Dispatcher.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(40);
        _timer.Tick += (_, _) => Tick();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        var stories = MailboxId == Mine
            ? _feed.MyStories()
            : _account.FindBubble(MailboxId) is { } bubble ? _feed.StoriesOf(bubble) : [];
        if (stories.Count == 0)
        {
            _ = Close();
            return;
        }
        _stories = stories;

        Progress.ColumnDefinitions.Clear();
        Progress.Children.Clear();
        _bars = [];
        for (var i = 0; i < stories.Count; i++)
        {
            Progress.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            var track = new Border { StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = 2 }, BackgroundColor = Color.FromArgb("#4DFFFFFF") };
            var bar = new BoxView { Color = Colors.White, AnchorX = 0, ScaleX = 0 };
            track.Content = bar;
            Grid.SetColumn(track, i);
            Progress.Children.Add(track);
            _bars.Add(bar);
        }

        // bei der ersten ungesehenen Story beginnen
        var firstUnseen = stories.Select((s, i) => (s, i)).FirstOrDefault(x => !_feed.IsSeen(x.s.Post.Id));
        Show(firstUnseen.s is null ? 0 : firstUnseen.i);
        _timer.Start();
    }

    protected override void OnDisappearing()
    {
        _timer.Stop();
        base.OnDisappearing();
    }

    void Show(int index)
    {
        _index = index;
        _shownAt = DateTime.UtcNow;
        for (var i = 0; i < _bars.Count; i++) _bars[i].ScaleX = i < index ? 1 : 0;

        var story = _stories[index];
        var m = story.Post.Message;
        _feed.MarkSeen(story.Post.Id);

        AuthorAvatar.Name = story.Author?.Name ?? m.AuthorName;
        AuthorAvatar.Image = story.Author?.Avatar;
        AuthorLabel.Text = story.Post.IsMine ? "Du" : story.Author?.Name ?? m.AuthorName;
        MetaLabel.Text = $"{story.Bubble.Name} · {Labels.Ago(m.CreatedAt)}";
        _current = new PostItem(story, _feed, showBubble: true);
        ShowReactions();
        MoodLabel.Text = Labels.Feeling(m);
        MoodChip.IsVisible = MoodLabel.Text.Length > 0;

        if (m.Image is { } bytes)
        {
            StoryImage.Source = ImageSource.FromStream(() => new MemoryStream(bytes));
            StoryImage.IsVisible = true;
            TextStage.IsVisible = false;
            Caption.Text = m.Text;
            Caption.IsVisible = m.Text is not null;
        }
        else
        {
            StoryImage.IsVisible = false;
            TextStage.IsVisible = true;
            TextStage.Background = Gradients.For(story.Post.Id);
            StoryText.Text = m.Text;
            Caption.IsVisible = false;
        }
        SemanticScreenReader.Announce($"{AuthorLabel.Text}: {m.Text}");
    }

    void ShowReactions()
    {
        if (_current is not { } item) return;
        LikeButton.Source = item.LikeIcon;
        SemanticProperties.SetDescription(LikeButton, item.LikeDescription);
        LikersLabel.Text = item.HasLikers ? $"♥ {item.LikersText}" : "";
        CommentButton.Text = item.CommentsText;
    }

    async void OnLike(object? sender, EventArgs e)
    {
        if (_current is not { } item) return;
        _ = Pop(LikeButton);
        var task = item.ToggleLikeAsync();
        ShowReactions(); // sofort anzeigen
        await task;
        if (_current == item) ShowReactions(); // nach einem Fehler steht hier wieder der alte Zustand
    }

    static async Task Pop(VisualElement view)
    {
        await view.ScaleToAsync(1.3, 90, Easing.CubicOut);
        await view.ScaleToAsync(1, 140, Easing.SpringOut);
    }

    /// <summary>Schließt die Story und öffnet das Highlight, direkt bei den Kommentaren.</summary>
    async void OnComments(object? sender, EventArgs e)
    {
        if (_current is not { } item) return;
        _timer.Stop();
        await Shell.Current.GoToAsync($"../{nameof(PostPage)}?id={Uri.EscapeDataString(item.MailboxId)}&post={Uri.EscapeDataString(item.Id)}&comments=1");
    }

    void Tick()
    {
        if (_bars.Count == 0) return;
        var progress = (DateTime.UtcNow - _shownAt) / PerStory;
        _bars[_index].ScaleX = Math.Min(1, progress);
        if (progress >= 1) Next();
    }

    void Next()
    {
        if (_index + 1 < _stories.Count) Show(_index + 1);
        else _ = Close();
    }

    protected override bool OnBackButtonPressed()
    {
        _ = Close();
        return true;
    }

    void OnNext(object? sender, TappedEventArgs e) => Next();

    void OnPrevious(object? sender, TappedEventArgs e) => Show(Math.Max(0, _index - 1));

    async void OnClose(object? sender, EventArgs e) => await Close();

    Task Close()
    {
        _timer.Stop();
        return Shell.Current.GoToAsync("..");
    }
}
