using System.ComponentModel;
using Bubble.App.Controls;
using Bubble.App.Services;
using Bubble.Core;

namespace Bubble.App.Pages;

/// <summary>A floating bubble on the home screen: "You" (share) or a bubble with stories from the last 24 h.</summary>
public sealed record StoryRing(string Title, string Name, byte[]? Image, AvatarRing Ring, bool ShowPlus, BubbleMembership? Bubble, int NewCount = 0)
{
    public bool IsMe => Bubble is null;
    public Color TitleColor => Ring == AvatarRing.New || IsMe ? Colors.White : Color.FromArgb("#8F89A0");
    public string Description => IsMe
        ? (Ring == AvatarRing.None ? "Share your highlight" : "View your stories")
        : $"Stories from {Title}";

    /// <summary>More new highlights make a bigger bubble.</summary>
    public double Size => NewCount == 0 ? 70 : 78 + 14 * Math.Min(NewCount, 4);
}

/// <summary>A period in the classic overview: a summary that opens to show its highlights.</summary>
public sealed class DigestItem(MemoryPeriod period, IReadOnlyList<PostItem> items, int bubbles, bool isOpen) : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public MemoryPeriod Period => period;
    public string Title => Labels.Period(period);
    public string Summary => bubbles == 1 ? $"{Labels.Highlights(items.Count)} from 1 bubble" : $"{Labels.Highlights(items.Count)} from {bubbles} bubbles";
    public string Detail { get; } = string.Join(' ', items.Select(i => i.Feeling.Split(' ')[0]).Where(e => e.Length > 0).Distinct().Take(4));
    public bool HasDetail => Detail.Length > 0;
    public IReadOnlyList<PostItem> Covers { get; } = items.Take(3).ToList();
    public string OpenAllText => $"Open all {items.Count}";
    public string Description => $"{Title}: {Summary}";

    public bool IsOpen
    {
        get => isOpen;
        set
        {
            isOpen = value;
            foreach (var name in new[] { nameof(IsOpen), nameof(Shown), nameof(ChevronRotation) })
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }

    /// <summary>Only filled while open: a closed card should not load its photos.</summary>
    public IReadOnlyList<PostItem> Shown => isOpen ? items.Take(6).ToList() : [];
    public double ChevronRotation => isOpen ? 270 : 90;
}

/// <summary>A filter above the box of memories. With a menu, tapping it again picks another month or year.</summary>
public sealed record PeriodChip(string Name, MemoryFilter Filter, bool IsOn, bool HasMenu)
{
    public Color BorderColor => Color.FromArgb(IsOn ? "#B9A2FF" : "#2E2A3A");
    public Color Fill => IsOn ? Color.FromArgb("#221F2C") : Colors.Transparent;
    public Color TextColor => Color.FromArgb(IsOn ? "#F5F3FA" : "#C9C4D6");
    public string Description => (IsOn ? $"{Name}, selected" : Name) + (HasMenu && IsOn ? ". Tap to choose another" : "");
}

public partial class HomePage : ContentPage
{
    readonly AccountService _account;
    readonly FeedService _feed;
    readonly PushService _push;
    readonly BubbleCluster _bubbles = new();
    bool _loading;
    HomeLayout? _layout;
    string _ringSig = "", _memorySig = "";
    MemoryPeriod? _openPeriod = MemoryPeriod.Week;
    MemoryFilter? _filter;
    int _boxYear = DateTime.Now.Year;
    IReadOnlyList<HomePost> _memories = [];

    public HomePage(AccountService account, FeedService feed, PushService push)
    {
        InitializeComponent();
        _account = account;
        _feed = feed;
        _push = push;
        _bubbles.RingTapped += OnRingTapped;
        _bubbles.PlusTapped += async (_, _) => await Compose();
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
        _bubbles.Start();
        Render(); // immediately, with whatever is already loaded
        ShareInbox.MarkReady();
        await _feed.LoadCachedAsync(); // last known posts right away, also without a connection
        Render();
        await _push.EnableByDefaultAsync();
        await LoadAsync();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _feed.Refreshed -= OnFeedRefreshed;
        _bubbles.Stop();
        Table.Stop();
    }

    void OnFeedRefreshed() => MainThread.BeginInvokeOnMainThread(() =>
    {
        if (_account.Vault is not null) Render();
    });

    async void OnRefreshing(object? sender, EventArgs e) => await LoadAsync();

    /// <summary>Dragging moves the table in the box, so pulling down can't refresh there.</summary>
    async void OnBoxRefresh(object? sender, EventArgs e)
    {
        BoxRefresh.IsVisible = false;
        BoxLoading.IsVisible = BoxLoading.IsRunning = true;
        await LoadAsync();
        BoxLoading.IsVisible = BoxLoading.IsRunning = false;
        BoxRefresh.IsVisible = true;
    }

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

        // Without a bubble there is nothing to rummage through: the classic view explains how to start
        var layout = bubbles.Count == 0 ? HomeLayout.Classic : HomeMode.Current;
        var layoutChanged = layout != _layout;
        if (layoutChanged) ApplyLayout(layout);

        RenderBubbles(vault);

        var memories = _feed.Memories();
        var sig = string.Join('|', memories.Select(PostSig)) + $"#{DateTime.Today:yyyyMMdd}";
        if (sig != _memorySig || layoutChanged)
        {
            _memorySig = sig;
            _memories = memories;
            if (layout == HomeLayout.Classic) RenderCards();
            else RenderBox(backToStart: false);
        }

        TodayLabel.IsVisible = bubbles.Count > 0;
        EmptyState.IsVisible = memories.Count == 0;
        if (bubbles.Count == 0)
        {
            EmptyTitle.Text = "Start your first bubble";
            EmptyText.Text = "A bubble is a small, private circle – family, flatmates, best friends. Create one or join with a QR code.";
            EmptyAction.Text = "Create bubble";
            EmptySecondary.IsVisible = true;
        }
        else
        {
            EmptyTitle.Text = "Still quiet here";
            EmptyText.Text = "Nothing shared yet. Highlights show up here, and stay if someone chooses to remember them.";
            EmptyAction.Text = "Share my highlight";
            EmptySecondary.IsVisible = false;
        }

        OfflineBar.IsVisible = _feed.Unreachable.Count > 0;
        OfflineLabel.Text = _feed.Unreachable.Count switch
        {
            0 => "",
            1 => $"“{_feed.Unreachable[0].Name}” is not reachable right now ({Labels.Host(_feed.Unreachable[0].RelayUrl)}).",
            var n => $"{n} bubbles are not reachable right now.",
        };
    }

    /// <summary>Moves the one set of floating bubbles to where this mode wants it.</summary>
    void ApplyLayout(HomeLayout layout)
    {
        _layout = layout;
        var box = layout == HomeLayout.Box;
        Refresher.IsVisible = !box;
        BoxStage.IsVisible = box;
        ClassicBubbles.Content = null;
        BoxBubbles.Content = null;
        _bubbles.SetSize(box ? 176 : 248, box ? 0.8 : 1);
        (box ? BoxBubbles : ClassicBubbles).Content = _bubbles;
    }

    void RenderBubbles(Vault vault)
    {
        // First "You" (my own posts of the last 24 hours), then bubbles with unseen stories, then seen ones
        var rings = new List<StoryRing>();
        var mine = _feed.MyStories();
        var myRing = mine.Count == 0 ? AvatarRing.None : mine.Any(s => !_feed.IsSeen(s.Post.Id)) ? AvatarRing.New : AvatarRing.Seen;
        rings.AddRange(vault.Bubbles
            .Select(b => (Bubble: b, Stories: _feed.StoriesOf(b)))
            .Where(x => x.Stories.Count > 0)
            .Select(x =>
            {
                var unseen = x.Stories.Count(s => !_feed.IsSeen(s.Post.Id));
                var cover = x.Stories.LastOrDefault(s => s.Post.Message.Image is not null)?.Post.Message.Image
                            ?? x.Stories[^1].Author?.Avatar;
                return new StoryRing(x.Bubble.Name, x.Bubble.Name, cover, unseen > 0 ? AvatarRing.New : AvatarRing.Seen, false, x.Bubble, unseen);
            })
            .OrderByDescending(r => r.NewCount));
        rings.Add(new StoryRing("You", vault.DisplayName, vault.Avatar, myRing, true, null));

        // Only replace them if something changed: replacing reloads all images and flickers.
        var ringSig = string.Join('|', rings.Select(r => $"{r.Title}:{r.Ring}:{r.NewCount}:{r.Image?.Length}:{r.ShowPlus}"));
        if (ringSig == _ringSig) return;
        _ringSig = ringSig;
        _bubbles.SetRings(rings);
    }

    void RenderCards()
    {
        var now = DateTimeOffset.Now;
        var cards = _memories
            .GroupBy(p => MemoryPeriods.Of(p.Post.Message.CreatedAt, now))
            .OrderBy(g => g.Key)
            .Select(g => new DigestItem(g.Key, g.Select(ToItem).ToList(), g.Select(p => p.Bubble.MailboxId).Distinct().Count(), g.Key == _openPeriod))
            .ToList();
        BindableLayout.SetItemsSource(Cards, cards);
    }

    void RenderBox(bool backToStart)
    {
        var now = DateTimeOffset.Now;
        var filter = _filter ??= DefaultFilter(now);
        var months = MonthsOf(_boxYear, now);
        var month = filter.Range == MemoryRange.Month ? filter : MemoryFilter.OfMonth(_boxYear, LikelyMonth(months, now));
        var year = MemoryFilter.OfYear(_boxYear);
        BindableLayout.SetItemsSource(Chips, new List<PeriodChip>
        {
            new(Labels.Filter(MemoryFilter.ThisWeek, now), MemoryFilter.ThisWeek, filter == MemoryFilter.ThisWeek, false),
            new(Labels.Filter(month, now), month, filter == month, months.Count > 1),
            new(Labels.Filter(year, now), year, filter == year, YearsWithMemories(now).Count > 1),
        });

        var shown = _memories.Where(p => filter.Matches(p.Post.Message.CreatedAt, now)).ToList();
        BoxCount.Text = shown.Count == 1 ? "1 memory" : $"{shown.Count} memories";
        BoxCount.IsVisible = shown.Count > 0;
        BoxEmpty.IsVisible = shown.Count == 0;
        BoxEmptyTitle.Text = _memories.Count == 0 ? "Still quiet here" : $"Nothing from {Labels.Filter(filter, now).ToLowerInvariant()}";
        BoxEmptyText.Text = _memories.Count == 0
            ? "Nothing shared yet. Highlights land in here, and stay if someone chooses to remember them."
            : "Try another month or year above.";
        Table.SetMemories(shown.Select(ToItem).ToList(), backToStart);
    }

    /// <summary>Opens where there is something to see: this week, otherwise the latest month or year with memories.</summary>
    MemoryFilter DefaultFilter(DateTimeOffset now)
    {
        if (_memories.Count == 0 || _memories.Any(p => MemoryFilter.ThisWeek.Matches(p.Post.Message.CreatedAt, now))) return MemoryFilter.ThisWeek;
        var latest = _memories[0].Post.Message.CreatedAt.ToOffset(now.Offset);
        _boxYear = latest.Year;
        return latest.Year == now.Year ? MemoryFilter.OfMonth(latest.Year, latest.Month) : MemoryFilter.OfYear(latest.Year);
    }

    IEnumerable<DateTimeOffset> MemoryDates(DateTimeOffset now) => _memories.Select(p => p.Post.Message.CreatedAt.ToOffset(now.Offset));

    IReadOnlyList<int> MonthsOf(int year, DateTimeOffset now) =>
        MemoryDates(now).Where(d => d.Year == year).Select(d => d.Month).Distinct().OrderDescending().ToList();

    IReadOnlyList<int> YearsWithMemories(DateTimeOffset now) =>
        MemoryDates(now).Select(d => d.Year).Distinct().OrderDescending().ToList();

    int LikelyMonth(IReadOnlyList<int> months, DateTimeOffset now) =>
        months.Contains(now.Month) && _boxYear == now.Year ? now.Month : months.Count > 0 ? months[0] : now.Month;

    async Task<MemoryFilter?> PickMonthAsync(DateTimeOffset now)
    {
        var months = MonthsOf(_boxYear, now);
        var options = months.Select(m => new SheetOption(
            _boxYear == now.Year ? Labels.MonthName(m) : $"{Labels.MonthName(m)} {_boxYear}",
            Labels.Highlights(MemoryDates(now).Count(d => d.Year == _boxYear && d.Month == m)),
            Selected: _filter == MemoryFilter.OfMonth(_boxYear, m))).ToList();
        return await SheetPage.ChooseAsync(this, "Pick a month", null, options) is { } index ? MemoryFilter.OfMonth(_boxYear, months[index]) : null;
    }

    async Task<MemoryFilter?> PickYearAsync(DateTimeOffset now)
    {
        var years = YearsWithMemories(now);
        var options = years.Select(y => new SheetOption(
            y.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Labels.Highlights(MemoryDates(now).Count(d => d.Year == y)),
            Selected: y == _boxYear)).ToList();
        if (await SheetPage.ChooseAsync(this, "Pick a year", null, options) is not { } index) return null;
        _boxYear = years[index]; // the month filter follows the year
        return MemoryFilter.OfYear(_boxYear);
    }

    PostItem ToItem(HomePost post) => new(post, _feed, showBubble: true);

    static string PostSig(HomePost p) =>
        $"{p.Post.Id}:{p.Snapshot.LikersOf(p.Post.Id).Count}:{p.Snapshot.IsLikedByMe(p.Post.Id)}:{p.Snapshot.CommentsOf(p.Post.Id).Count}";

    async void OnRingTapped(object? sender, StoryRing ring)
    {
        if (ring.IsMe)
        {
            // View own stories; share right away if there are none
            if (ring.Ring == AvatarRing.None) await Compose();
            else await Shell.Current.GoToAsync($"{nameof(StoryPage)}?id={StoryPage.Mine}");
        }
        else await Shell.Current.GoToAsync($"{nameof(StoryPage)}?id={Uri.EscapeDataString(ring.Bubble!.MailboxId)}");
    }

    void OnDigestTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is not DigestItem card) return;
        var opening = !card.IsOpen;
        _openPeriod = opening ? card.Period : null;
        if (BindableLayout.GetItemsSource(Cards) is IEnumerable<DigestItem> cards)
            foreach (var other in cards.Where(c => c != card && c.IsOpen)) other.IsOpen = false;
        card.IsOpen = opening;
    }

    async void OnMemoryTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is PostItem post) await OpenPost(post);
    }

    async void OnTableMemoryTapped(object? sender, PostItem post) => await OpenPost(post);

    static Task OpenPost(PostItem post) =>
        Shell.Current.GoToAsync($"{nameof(PostPage)}?id={Uri.EscapeDataString(post.MailboxId)}&post={Uri.EscapeDataString(post.Id)}");

    async void OnOpenAll(object? sender, EventArgs e)
    {
        if ((sender as Button)?.CommandParameter is DigestItem card)
            await Shell.Current.GoToAsync($"{nameof(MemoriesPage)}?period={card.Period}");
    }

    async void OnOpenBox(object? sender, EventArgs e)
    {
        if (_filter is { } filter) await Shell.Current.GoToAsync($"{nameof(MemoriesPage)}?filter={filter.Encode()}");
    }

    async void OnChipTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is not PeriodChip chip) return;
        MemoryFilter? chosen = chip.Filter;
        if (chip.IsOn)
        {
            if (!chip.HasMenu) return;
            var now = DateTimeOffset.Now;
            chosen = chip.Filter.Range == MemoryRange.Month ? await PickMonthAsync(now) : await PickYearAsync(now);
            if (chosen is null) return;
        }
        _filter = chosen;
        RenderBox(backToStart: true);
    }

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

    async void OnEmptyAction(object? sender, EventArgs e)
    {
        if (_account.Vault!.Bubbles.Count == 0) await BubblesPage.CreateBubbleFlowAsync(this, _account);
        else await Compose();
    }
}
