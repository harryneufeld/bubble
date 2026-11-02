using Bubble.App.Services;
using Bubble.Core;

namespace Bubble.App.Pages;

[QueryProperty(nameof(Period), "period")]
[QueryProperty(nameof(Filter), "filter")]
public partial class MemoriesPage : ContentPage
{
    readonly FeedService _feed;

    /// <summary>A <see cref="MemoryPeriod"/> name; empty shows everything.</summary>
    public string Period { get; set; } = "";

    /// <summary>An encoded <see cref="MemoryFilter"/>, from the box of memories. Wins over the period.</summary>
    public string Filter { get; set; } = "";

    public MemoriesPage(FeedService feed)
    {
        InitializeComponent();
        _feed = feed;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        var now = DateTimeOffset.Now;
        var filter = MemoryFilter.TryParse(Filter, out var f) ? f : (MemoryFilter?)null;
        var period = Enum.TryParse<MemoryPeriod>(Period, out var parsed) ? parsed : (MemoryPeriod?)null;
        Title = filter is { } shown ? Labels.Filter(shown, now) : period is { } p ? Labels.Period(p) : "Box of memories";
        Grid.ItemsSource = _feed.Memories()
            .Where(m => filter?.Matches(m.Post.Message.CreatedAt, now) ?? period is null || MemoryPeriods.Of(m.Post.Message.CreatedAt, now) == period)
            .Select(m => new PostItem(m, _feed, showBubble: true))
            .ToList();
    }

    async void OnMemoryTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is PostItem post)
            await Shell.Current.GoToAsync($"{nameof(PostPage)}?id={Uri.EscapeDataString(post.MailboxId)}&post={Uri.EscapeDataString(post.Id)}");
    }
}
