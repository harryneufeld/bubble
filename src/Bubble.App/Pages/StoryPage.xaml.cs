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
        if (_account.FindBubble(MailboxId) is not { } bubble || _feed.StoriesOf(bubble) is not { Count: > 0 } stories)
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
        var firstUnseen = stories.Select((s, i) => (s, i)).FirstOrDefault(x => !x.s.Post.IsMine && !_feed.IsSeen(x.s.Post.Id));
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
