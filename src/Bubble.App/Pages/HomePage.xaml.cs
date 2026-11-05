using System.Globalization;
using Bubble.App.Controls;
using Bubble.App.Services;
using Bubble.Core;
using Microsoft.Maui.Controls.Shapes;

namespace Bubble.App.Pages;

/// <summary>A circle in the story bar: "You" (share) or a bubble with stories from the last 24 h.</summary>
public sealed record StoryRing(string Title, string Name, byte[]? Image, AvatarRing Ring, bool ShowPlus, BubbleMembership? Bubble)
{
    public bool IsMe => Bubble is null;
    public Color TitleColor => Ring == AvatarRing.New || IsMe ? Colors.White : Color.FromArgb("#8F89A0");
    public string Description => IsMe
        ? (Ring == AvatarRing.None ? "Share your highlight" : "View your stories")
        : $"Stories from {Title}";
}

public partial class HomePage : ContentPage
{
    static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    readonly AccountService _account;
    readonly FeedService _feed;
    bool _loading;
    string _ringSig = "", _postSig = "";
    IReadOnlyList<HomePost> _posts = [];
    DateTimeOffset _now = DateTimeOffset.Now;
    DateTime _weekDay = DateTime.Today;
    DateTime? _monthDay = DateTime.Today;
    (int Year, int Month) _month = (DateTime.Today.Year, DateTime.Today.Month);
    readonly List<(DateTime Date, Border View)> _weekCells = [], _monthCells = [];
    HomePost? _recall;

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
        _feed.Refreshed -= OnFeedRefreshed;
        _feed.Refreshed += OnFeedRefreshed;
        Render(); // immediately, with whatever is already loaded
        ShareInbox.MarkReady();
        await _feed.LoadCachedAsync(); // last known posts right away, also without a connection
        Render();
        await LoadAsync();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _feed.Refreshed -= OnFeedRefreshed;
    }

    void OnFeedRefreshed() => MainThread.BeginInvokeOnMainThread(() =>
    {
        if (_account.Vault is not null) Render();
    });

    async void OnRefreshing(object? sender, EventArgs e) => await LoadAsync();

    async Task LoadAsync()
    {
        if (_loading) return;
        _loading = true;
        try
        {
            if (_account.Vault!.Bubbles.Count > 0) await _feed.RefreshAsync();
            Render();
            foreach (var name in _feed.TakeRemoved())
                await this.ShowAlertAsync("Removed from bubble", $"The creator removed you from “{name}”.", "OK");
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

        // Stories: first "You" (my own posts of the last 24 hours), then bubbles with unseen stories, then seen ones
        var rings = new List<StoryRing>();
        var mine = _feed.MyStories();
        var myRing = mine.Count == 0 ? AvatarRing.None : mine.Any(s => !_feed.IsSeen(s.Post.Id)) ? AvatarRing.New : AvatarRing.Seen;
        rings.Add(new StoryRing("You", vault.DisplayName, vault.Avatar, myRing, true, null));
        rings.AddRange(bubbles
            .Select(b => (Bubble: b, Stories: _feed.StoriesOf(b)))
            .Where(x => x.Stories.Count > 0)
            .Select(x =>
            {
                var unseen = x.Stories.Any(s => !_feed.IsSeen(s.Post.Id));
                var cover = x.Stories.LastOrDefault(s => s.Post.Message.Image is not null)?.Post.Message.Image
                            ?? x.Stories[^1].Author?.Avatar;
                return new StoryRing(x.Bubble.Name, x.Bubble.Name, cover, unseen ? AvatarRing.New : AvatarRing.Seen, false, x.Bubble);
            })
            .OrderByDescending(r => r.Ring == AvatarRing.New));

        // Only replace lists if something changed: replacing reloads all images and flickers.
        var ringSig = string.Join('|', rings.Select(r => $"{r.Title}:{r.Ring}:{r.Image?.Length}:{r.ShowPlus}"));
        if (ringSig != _ringSig)
        {
            _ringSig = ringSig;
            BindableLayout.SetItemsSource(StoriesRow, rings);
        }

        var hasBubbles = bubbles.Count > 0;
        EmptyState.IsVisible = !hasBubbles;
        WeekCard.IsVisible = MonthCard.IsVisible = RewindCard.IsVisible = hasBubbles;

        var posts = _feed.AllPosts();
        var postSig = string.Join('|', posts.Select(p => p.Post.Id)) + $"#{DateTime.Today:yyyyMMdd}";
        if (postSig != _postSig)
        {
            _postSig = postSig;
            _posts = posts;
            _now = DateTimeOffset.Now;
            RenderWeek();
            RenderRecall();
            RenderMonth();
            RenderRewind();
        }
        if (!hasBubbles) RecallCard.IsVisible = false;

        OfflineBar.IsVisible = _feed.Unreachable.Count > 0;
        OfflineLabel.Text = _feed.Unreachable.Count switch
        {
            0 => "",
            1 => $"“{_feed.Unreachable[0].Name}” is not reachable right now ({Labels.Host(_feed.Unreachable[0].RelayUrl)}).",
            var n => $"{n} bubbles are not reachable right now.",
        };
    }

    DateTime DateOf(HomePost post) => Timeline.LocalDate(post.Post.Message.CreatedAt, _now);

    List<HomePost> PostsOn(DateTime date) => _posts.Where(p => DateOf(p) == date).ToList();

    PostItem ToItem(HomePost post) => new(post, _feed, showBubble: true);

    // ── This week ─────────────────────────────────────────

    void RenderWeek()
    {
        var start = Timeline.WeekStart(_now).Date;
        WeekRange.Text = $"{start.ToString("d MMM", Invariant)} – {start.AddDays(6).ToString("d MMM", Invariant)}";
        WeekDays.Children.Clear();
        _weekCells.Clear();
        if (_weekDay < start || _weekDay > DateTime.Today) _weekDay = DateTime.Today;
        // A week without photos shows just its dates, not seven empty stacks
        var anyPhotos = Enumerable.Range(0, 7).Any(i => PostsOn(start.AddDays(i)).Count > 0);

        for (var i = 0; i < 7; i++)
        {
            var date = start.AddDays(i);
            var posts = PostsOn(date);
            var future = date > DateTime.Today;

            var stack = new Grid { WidthRequest = 38, HeightRequest = 62, HorizontalOptions = LayoutOptions.Center, InputTransparent = true, IsVisible = anyPhotos };
            double[] tilt = [-6, 5, -3];
            for (var k = 0; k < Math.Min(3, posts.Count); k++)
            {
                var tile = Tile(ToItem(posts[k]), 34, 34, 7);
                tile.Stroke = (Color)Application.Current!.Resources["Surface"];
                tile.StrokeThickness = 2;
                tile.Rotation = tilt[k];
                tile.Margin = new Thickness(2 + k % 2 * 2, k * 13, 0, 0);
                stack.Add(tile);
            }

            var isToday = date == DateTime.Today;
            var number = new Border
            {
                StrokeShape = new Ellipse(), WidthRequest = 28, HeightRequest = 28, HorizontalOptions = LayoutOptions.Center,
                BackgroundColor = isToday ? (Color)Application.Current!.Resources["Primary"] : Colors.Transparent,
                Content = new Label { Text = date.Day.ToString(Invariant), FontFamily = "BodyBold", FontSize = 14, HorizontalOptions = LayoutOptions.Center, VerticalOptions = LayoutOptions.Center },
            };
            var cell = new Border
            {
                StrokeShape = new RoundRectangle { CornerRadius = 16 },
                Padding = new Thickness(0, 8),
                Opacity = future ? 0.4 : 1,
                Content = new VerticalStackLayout
                {
                    Spacing = 6,
                    Children =
                    {
                        new Label { Text = date.ToString("ddd", Invariant), FontSize = 11, FontFamily = "BodyMedium", TextColor = (Color)Application.Current!.Resources["InkSoft"], HorizontalOptions = LayoutOptions.Center },
                        number,
                        stack,
                        new Label { Text = posts.Count > 3 ? $"+{posts.Count - 3}" : posts.Count == 0 ? "·" : " ", Style = (Style)Application.Current.Resources["CaptionText"], FontSize = 11, HorizontalOptions = LayoutOptions.Center, IsVisible = anyPhotos },
                    },
                },
            };
            SemanticProperties.SetDescription(cell, $"{date.ToString("dddd d MMMM", Invariant)}, {Labels.Highlights(posts.Count)}");
            if (!future) cell.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(() => SelectWeekDay(date)) });
            WeekDays.Add(cell, i, 0);
            _weekCells.Add((date, cell));
        }
        SelectWeekDay(_weekDay);
    }

    void SelectWeekDay(DateTime date)
    {
        _weekDay = date;
        foreach (var (day, view) in _weekCells)
            view.BackgroundColor = day == date ? (Color)Application.Current!.Resources["SurfaceRaised"] : Colors.Transparent;
        var posts = PostsOn(date);
        var name = date == DateTime.Today ? "Today" : date.ToString("dddd", Invariant);
        WeekDayTitle.Text = posts.Count == 0 ? $"{name} · nothing shared" : $"{name} · {Labels.Highlights(posts.Count)}";
        BindableLayout.SetItemsSource(WeekDayPosts, posts.Select(ToItem).ToList());
    }

    // ── On this day ───────────────────────────────────────

    void RenderRecall()
    {
        var recall = Timeline.OnThisDay(_posts, p => p.Post.Message.CreatedAt, _now);
        RecallCard.IsVisible = recall is not null;
        _recall = recall?.Items[0];
        if (recall is null || _recall is null) return;

        var item = ToItem(_recall);
        RecallTitle.Text = Labels.Recall(recall.MonthsAgo);
        RecallCaption.Text = item.Text ?? item.Feeling;
        RecallCaption.IsVisible = RecallCaption.Text.Length > 0;
        RecallMeta.Text = recall.Items.Count > 1 ? $"{item.Author} · {item.BubbleName} · and {recall.Items.Count - 1} more" : $"{item.Author} · {item.BubbleName}";
        RecallPhoto.Content = Tile(item, 84, 84, 0);
    }

    async void OnRecallTapped(object? sender, TappedEventArgs e)
    {
        if (_recall is { } post) await OpenPost(post.Bubble.MailboxId, post.Post.Id);
    }

    // ── The month ─────────────────────────────────────────

    (int Year, int Month) EarliestMonth()
    {
        if (_posts.Count == 0) return (DateTime.Today.Year, DateTime.Today.Month);
        var first = _posts.Min(DateOf);
        return (first.Year, first.Month);
    }

    void RenderMonth()
    {
        var today = DateTime.Today;
        var earliest = EarliestMonth();
        if (_month.CompareTo(earliest) < 0) _month = earliest;
        if (_month.CompareTo((today.Year, today.Month)) > 0) _month = (today.Year, today.Month);

        MonthTitle.Text = _month == (today.Year, today.Month) ? "This month" : Labels.Month(_month.Year, _month.Month);
        PrevMonth.IsEnabled = _month.CompareTo(earliest) > 0;
        NextMonth.IsEnabled = _month.CompareTo((today.Year, today.Month)) < 0;
        PrevMonth.Opacity = PrevMonth.IsEnabled ? 1 : 0.3;
        NextMonth.Opacity = NextMonth.IsEnabled ? 1 : 0.3;

        Calendar.Children.Clear();
        Calendar.RowDefinitions.Clear();
        _monthCells.Clear();
        var resources = Application.Current!.Resources;
        string[] letters = ["M", "T", "W", "T", "F", "S", "S"];
        Calendar.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        for (var c = 0; c < 7; c++)
            Calendar.Add(new Label { Text = letters[c], Style = (Style)resources["CaptionText"], FontSize = 11, HorizontalTextAlignment = TextAlignment.Center }, c, 0);

        var first = new DateTime(_month.Year, _month.Month, 1);
        var offset = ((int)first.DayOfWeek + 6) % 7;
        var days = DateTime.DaysInMonth(_month.Year, _month.Month);
        for (var r = 0; r < (offset + days + 6) / 7; r++) Calendar.RowDefinitions.Add(new RowDefinition(46));

        for (var d = 1; d <= days; d++)
        {
            var date = first.AddDays(d - 1);
            var posts = PostsOn(date);
            var grid = new Grid();
            if (posts.Count > 0)
            {
                var tile = Tile(ToItem(posts[0]), -1, -1, 0);
                grid.Add(tile);
                grid.Add(new BoxView { Color = Color.FromArgb("#4D0B0A10") });
            }
            grid.Add(new Label
            {
                Text = d.ToString(Invariant), FontSize = 11, Margin = new Thickness(5, 3),
                FontFamily = posts.Count > 0 || date == today ? "BodyBold" : "Body",
                TextColor = posts.Count > 0 ? Colors.White : date == today ? (Color)resources["Accent"] : (Color)resources["Muted"],
            });
            if (posts.Count > 1)
                grid.Add(new Ellipse { Fill = Colors.White, WidthRequest = 6, HeightRequest = 6, HorizontalOptions = LayoutOptions.End, VerticalOptions = LayoutOptions.End, Margin = 4 });

            var cell = new Border
            {
                StrokeShape = new RoundRectangle { CornerRadius = 10 },
                BackgroundColor = (Color)resources["SurfaceRaised"],
                Content = grid,
            };
            SemanticProperties.SetDescription(cell, $"{date.ToString("d MMMM", Invariant)}, {Labels.Highlights(posts.Count)}");
            if (posts.Count > 0) cell.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(() => SelectMonthDay(date)) });
            var index = offset + d - 1;
            Calendar.Add(cell, index % 7, 1 + index / 7);
            _monthCells.Add((date, cell));
        }

        if (_monthDay is { } selected && (selected.Year != _month.Year || selected.Month != _month.Month || PostsOn(selected).Count == 0))
            _monthDay = null;
        SelectMonthDay(_monthDay);
    }

    void SelectMonthDay(DateTime? date)
    {
        _monthDay = date;
        foreach (var (day, view) in _monthCells)
        {
            view.Stroke = day == date ? (Color)Application.Current!.Resources["Ink"] : Colors.Transparent;
            view.StrokeThickness = day == date ? 2 : 0;
        }
        var posts = date is { } d ? PostsOn(d) : [];
        MonthDayTitle.Text = date is { } day2 ? $"{day2.ToString("d MMMM", Invariant)} · {Labels.Highlights(posts.Count)}" : "Tap a day with photos";
        BindableLayout.SetItemsSource(MonthDayPosts, posts.Select(ToItem).ToList());
    }

    void OnPrevMonth(object? sender, EventArgs e)
    {
        var d = new DateTime(_month.Year, _month.Month, 1).AddMonths(-1);
        _month = (d.Year, d.Month);
        RenderMonth();
    }

    void OnNextMonth(object? sender, EventArgs e)
    {
        var d = new DateTime(_month.Year, _month.Month, 1).AddMonths(1);
        _month = (d.Year, d.Month);
        RenderMonth();
    }

    // ── Rewind ────────────────────────────────────────────

    void RenderRewind()
    {
        var months = Timeline.LastMonths(_now, 12);
        var counts = months.Select(m => _posts.Count(p => DateOf(p) is var d && d.Year == m.Year && d.Month == m.Month)).ToList();
        var max = Math.Max(1, counts.Max());
        var resources = Application.Current!.Resources;

        Bars.Children.Clear();
        Bars.ColumnDefinitions.Clear();
        for (var i = 0; i < months.Count; i++)
        {
            Bars.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            Bars.Add(new BoxView
            {
                HeightRequest = 4 + 36.0 * counts[i] / max,
                VerticalOptions = LayoutOptions.End,
                CornerRadius = 3,
                Color = counts[i] > 0 ? (Color)resources["Violet"] : (Color)resources["Hairline"],
            }, i, 0);
        }
        BarsFrom.Text = Labels.Month(months[0].Year, months[0].Month);

        if (_posts.Count == 0)
        {
            RewindText.Text = "Nothing shared yet. Everything that is still there shows up here, month by month.";
            return;
        }
        var earliest = EarliestMonth();
        RewindText.Text = $"{Labels.Highlights(_posts.Count)} since {Labels.Month(earliest.Year, earliest.Month)}. Scrub back through time, or find someone.";
    }

    async void OnRewind(object? sender, TappedEventArgs e) => await Shell.Current.GoToAsync(nameof(RewindPage));

    // ── Tiles ─────────────────────────────────────────────

    /// <summary>A photo (as a small thumbnail) or a text highlight's gradient, in a rounded frame. Size -1 fills the cell.</summary>
    static Border Tile(PostItem item, double width, double height, double radius)
    {
        var border = new Border
        {
            StrokeShape = new RoundRectangle { CornerRadius = radius },
            StrokeThickness = 0,
            Background = item.TextBackground,
            WidthRequest = width,
            HeightRequest = height,
            HorizontalOptions = width < 0 ? LayoutOptions.Fill : LayoutOptions.Start,
            VerticalOptions = height < 0 ? LayoutOptions.Fill : LayoutOptions.Start,
            InputTransparent = true,
        };
        if (item.Source.Post.Message.Image is { Length: > 0 } photo)
        {
            var image = new FadeImage { Aspect = Aspect.AspectFill };
            border.Content = image;
            _ = ShowThumbnailAsync(image, photo);
        }
        return border;
    }

    static async Task ShowThumbnailAsync(Image image, byte[] photo) => image.Source = await Thumbnails.ForAsync(photo);

    // ── Navigation ────────────────────────────────────────

    async void OnStoryTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is not StoryRing ring) return;
        if (ring.IsMe)
        {
            // View own stories; share right away if there are none
            if (ring.Ring == AvatarRing.None) await Compose();
            else await Shell.Current.GoToAsync($"{nameof(StoryPage)}?id={StoryPage.Mine}");
        }
        else await Shell.Current.GoToAsync($"{nameof(StoryPage)}?id={Uri.EscapeDataString(ring.Bubble!.MailboxId)}");
    }

    async void OnPlusTapped(object? sender, EventArgs e) => await Compose();

    async void OnPostTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is PostItem post) await OpenPost(post.MailboxId, post.Id);
    }

    static Task OpenPost(string mailboxId, string postId) =>
        Shell.Current.GoToAsync($"{nameof(PostPage)}?id={Uri.EscapeDataString(mailboxId)}&post={Uri.EscapeDataString(postId)}");

    async void OnCompose(object? sender, EventArgs e) => await Compose();

    async Task Compose()
    {
        if (_account.Vault!.Bubbles.Count == 0)
        {
            await this.ShowAlertAsync("No bubble yet", "Create a bubble first or join one.", "OK");
            return;
        }
        await Shell.Current.GoToAsync(nameof(ComposePage));
    }

    async void OnJoin(object? sender, EventArgs e) => await Shell.Current.GoToAsync(nameof(JoinPage));

    async void OnCreateBubble(object? sender, EventArgs e) => await BubblesPage.CreateBubbleFlowAsync(this, _account);
}
