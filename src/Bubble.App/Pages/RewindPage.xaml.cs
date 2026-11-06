using Bubble.App.Services;
using Bubble.Core;

namespace Bubble.App.Pages;

/// <summary>Whose highlights Rewind shows: an empty key means all, "me" only mine, otherwise a bubble's mailbox.</summary>
public sealed record ScopeChip(string Key, string Name, byte[]? Image, bool IsOn)
{
    public Color Ring => IsOn ? Color.FromArgb("#B9A2FF") : Colors.Transparent;
    public Color TextColor => Color.FromArgb(IsOn ? "#F5F3FA" : "#C9C4D6");
    public string Description => (Key.Length == 0 ? "Highlights from all bubbles" : Key == "me" ? "Only your own highlights" : $"Only highlights in {Name}") + (IsOn ? ", selected" : "");
}

/// <summary>One month in Rewind.</summary>
public sealed class MonthItems(string title, IReadOnlyList<PostItem> items) : List<PostItem>(items)
{
    public string Title => title;
    public string Summary => Labels.Highlights(Count);
}

public partial class RewindPage : ContentPage
{
    // The list has fixed sizes, so the month at a scroll position can be worked out without measuring
    const double HeaderHeight = 44, RowHeight = 112 + 6, KnobHeight = 28, RailInset = 8;

    readonly FeedService _feed;
    string _scope = "";
    List<MonthItems> _months = [];
    List<double> _tops = [];
    double _total, _fraction, _dragStart;
    bool _dragging;

    public RewindPage(FeedService feed)
    {
        InitializeComponent();
        _feed = feed;

        var pan = new PanGestureRecognizer();
        pan.PanUpdated += OnScrub;
        ScrubArea.GestureRecognizers.Add(pan);
        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, e) =>
        {
            if (e.GetPosition(ScrubArea) is { } at) JumpTo((at.Y - RailInset - KnobHeight / 2) / Travel);
        };
        ScrubArea.GestureRecognizers.Add(tap);
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        Render();
    }

    bool InScope(HomePost post) => _scope switch
    {
        "" => true,
        "me" => post.Post.IsMine,
        var mailbox => post.Bubble.MailboxId == mailbox,
    };

    void Render()
    {
        var now = DateTimeOffset.Now;
        var all = _feed.AllPosts();

        // All, mine, then the bubbles by how much they hold, each with its newest photo as the circle
        var scopes = new List<ScopeChip> { new("", "All", null, _scope.Length == 0) };
        if (all.FirstOrDefault(p => p.Post.IsMine) is { } mine) scopes.Add(new("me", "Mine", mine.Author?.Avatar, _scope == "me"));
        scopes.AddRange(all.GroupBy(p => p.Bubble.MailboxId)
            .OrderByDescending(g => g.Count())
            .Select(g => new ScopeChip(g.Key, g.First().Bubble.Name, g.FirstOrDefault(p => p.Post.Message.Image is not null)?.Post.Message.Image, _scope == g.Key)));
        BindableLayout.SetItemsSource(Scopes, scopes);

        _months = Timeline.ByMonth(all.Where(InScope), p => p.Post.Message.CreatedAt, now)
            .Select(m => new MonthItems(Labels.Month(m.Year, m.Month), m.Items.Select(p => new PostItem(p, _feed, showBubble: true)).ToList()))
            .ToList();
        _tops = [];
        _total = 0;
        foreach (var month in _months)
        {
            _tops.Add(_total);
            _total += HeaderHeight + Math.Ceiling(month.Count / 3.0) * RowHeight;
        }
        Months.ItemsSource = _months;
        ScrubArea.IsVisible = Knob.IsVisible = _months.Count > 1;
        PlaceKnob(0);
    }

    double Travel => Math.Max(1, ScrubArea.Height - 2 * RailInset - KnobHeight);

    void PlaceKnob(double fraction)
    {
        _fraction = Math.Clamp(fraction, 0, 1);
        Knob.TranslationY = _fraction * Travel;
        ScrubLabel.TranslationY = _fraction * Travel;
    }

    int MonthAt(double fraction)
    {
        var y = fraction * Math.Max(0, _total - Months.Height);
        var index = 0;
        for (var i = 0; i < _tops.Count; i++)
            if (_tops[i] <= y + 20) index = i;
        return index;
    }

    void OnScrolled(object? sender, ItemsViewScrolledEventArgs e)
    {
        if (_dragging) return;
        PlaceKnob(e.VerticalOffset / Math.Max(1, _total - Months.Height));
    }

    void OnScrub(object? sender, PanUpdatedEventArgs e)
    {
        switch (e.StatusType)
        {
            case GestureStatus.Started:
                _dragging = true;
                _dragStart = _fraction;
                ScrubLabel.IsVisible = true;
                break;
            case GestureStatus.Running:
                JumpTo(_dragStart + e.TotalY / Travel);
                break;
            default:
                _dragging = false;
                ScrubLabel.IsVisible = false;
                break;
        }
    }

    void JumpTo(double fraction)
    {
        if (_months.Count == 0) return;
        PlaceKnob(fraction);
        var month = MonthAt(_fraction);
        ScrubText.Text = _months[month].Title;
        Months.ScrollTo(0, month, ScrollToPosition.Start, animate: false);
    }

    void OnScopeTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is not ScopeChip chip || chip.IsOn) return;
        _scope = chip.Key;
        Render();
    }

    async void OnPostTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is PostItem post) await Open(post);
    }

    /// <summary>A random highlight from what the filters show.</summary>
    async void OnSurprise(object? sender, EventArgs e)
    {
        var all = _months.SelectMany(m => m).ToList();
        if (all.Count > 0) await Open(all[Random.Shared.Next(all.Count)]);
    }

    static Task Open(PostItem post) =>
        Shell.Current.GoToAsync($"{nameof(PostPage)}?id={Uri.EscapeDataString(post.MailboxId)}&post={Uri.EscapeDataString(post.Id)}");
}
