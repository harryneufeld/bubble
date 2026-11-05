using Bubble.Core;

namespace Bubble.Tests;

public class TimelineTests
{
    // Thursday, 22 October 2026
    static readonly DateTimeOffset Now = new(2026, 10, 22, 12, 0, 0, TimeSpan.FromHours(2));

    [Fact]
    public void TheWeek_StartsOnMonday_WhateverDayItIs()
    {
        var monday = new DateTimeOffset(2026, 10, 19, 0, 0, 0, TimeSpan.FromHours(2));
        Assert.Equal(monday, Timeline.WeekStart(Now));
        Assert.Equal(monday, Timeline.WeekStart(monday));
        Assert.Equal(monday, Timeline.WeekStart(new DateTimeOffset(2026, 10, 25, 23, 0, 0, TimeSpan.FromHours(2)))); // Sunday
    }

    [Fact]
    public void Dates_AreCountedInTheLocalCalendar()
    {
        // 30 Sep 23:00 UTC is already 1 Oct at +02:00
        Assert.Equal(new DateTime(2026, 10, 1), Timeline.LocalDate(new DateTimeOffset(2026, 9, 30, 23, 0, 0, TimeSpan.Zero), Now));
    }

    [Fact]
    public void Months_AreNewestFirst_AndOnlyThoseWithHighlights()
    {
        DateTimeOffset[] posts = [Now.AddMonths(-13), Now, Now.AddDays(-1), Now.AddMonths(-2)];
        var months = Timeline.ByMonth(posts, p => p, Now);

        Assert.Equal([(2026, 10), (2026, 8), (2025, 9)], months.Select(m => (m.Year, m.Month)));
        Assert.Equal([Now, Now.AddDays(-1)], months[0].Items);
    }

    [Fact]
    public void OnThisDay_PrefersAYearAgo_OverAMonthAgo()
    {
        DateTimeOffset[] posts = [Now.AddMonths(-1), Now.AddYears(-1).AddHours(3), Now.AddYears(-2)];
        var recall = Timeline.OnThisDay(posts, p => p, Now);

        Assert.NotNull(recall);
        Assert.Equal(12, recall.MonthsAgo);
        Assert.Single(recall.Items);
    }

    [Fact]
    public void OnThisDay_FallsBackToAMonthAgo_AndOtherwiseShowsNothing()
    {
        Assert.Equal(1, Timeline.OnThisDay([Now.AddMonths(-1)], p => p, Now)?.MonthsAgo);
        Assert.Equal(24, Timeline.OnThisDay([Now.AddYears(-2)], p => p, Now)?.MonthsAgo);
        Assert.Null(Timeline.OnThisDay([Now.AddDays(-3), Now.AddMonths(-1).AddDays(1)], p => p, Now));
    }

    [Fact]
    public void LastMonths_EndWithThisOne()
    {
        Assert.Equal([(2025, 11), (2025, 12), (2026, 1)], Timeline.LastMonths(new DateTimeOffset(2026, 1, 31, 12, 0, 0, TimeSpan.Zero), 3));
    }
}
