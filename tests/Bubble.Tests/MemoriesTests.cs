using Bubble.Core;

namespace Bubble.Tests;

public class MemoriesTests
{
    static readonly DateTimeOffset Now = new(2026, 10, 22, 12, 0, 0, TimeSpan.FromHours(2));

    [Theory]
    [InlineData(0, MemoryPeriod.Week)]
    [InlineData(3, MemoryPeriod.Week)]
    [InlineData(4, MemoryPeriod.Month)]
    [InlineData(21, MemoryPeriod.Month)]
    [InlineData(22, MemoryPeriod.Year)]
    [InlineData(294, MemoryPeriod.Year)]
    [InlineData(296, MemoryPeriod.Older)]
    public void EachHighlightFallsIntoOnePeriod(int daysAgo, MemoryPeriod expected) =>
        Assert.Equal(expected, MemoryPeriods.Of(Now.AddDays(-daysAgo), Now));

    [Fact]
    public void AtTheStartOfAYear_LastWeekStaysInTheWeekNotInOlder()
    {
        var newYear = new DateTimeOffset(2027, 1, 2, 12, 0, 0, TimeSpan.Zero);
        Assert.Equal(MemoryPeriod.Week, MemoryPeriods.Of(newYear.AddDays(-3), newYear));
    }

    [Fact]
    public void Periods_UseTheLocalCalendarOfNow()
    {
        var now = new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.FromHours(2));
        // 30 Sep 23:00 UTC is already 1 Oct at +02:00
        var created = new DateTimeOffset(2026, 9, 30, 23, 0, 0, TimeSpan.Zero);
        Assert.Equal(MemoryPeriod.Month, MemoryPeriods.Of(created, now));
    }

    [Fact]
    public void TheWeek_StartsOnMonday_WhateverDayItIs()
    {
        var monday = new DateTimeOffset(2026, 10, 19, 0, 0, 0, TimeSpan.FromHours(2));
        Assert.Equal(monday, MemoryPeriods.WeekStart(Now));
        Assert.Equal(monday, MemoryPeriods.WeekStart(monday));
        Assert.Equal(monday, MemoryPeriods.WeekStart(new DateTimeOffset(2026, 10, 25, 23, 0, 0, TimeSpan.FromHours(2)))); // Sunday
        Assert.False(MemoryFilter.ThisWeek.Matches(monday.AddSeconds(-1), Now));
        Assert.True(MemoryFilter.ThisWeek.Matches(monday, Now));
    }

    [Fact]
    public void Filters_OverlapByDesign_AMonthContainsItsWeek()
    {
        Assert.True(MemoryFilter.OfMonth(2026, 10).Matches(Now, Now));
        Assert.True(MemoryFilter.OfYear(2026).Matches(Now, Now));
        Assert.False(MemoryFilter.OfMonth(2026, 9).Matches(Now, Now));
        Assert.False(MemoryFilter.OfMonth(2025, 10).Matches(Now, Now));
        Assert.True(MemoryFilter.OfYear(2025).Matches(Now.AddYears(-1), Now));
    }

    [Theory]
    [InlineData("Week")]
    [InlineData("Month-2026-10")]
    [InlineData("Year-2025")]
    public void Filters_SurviveBeingPassedToAnotherPage(string text)
    {
        Assert.True(MemoryFilter.TryParse(text, out var filter));
        Assert.Equal(text, filter.Encode());
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("Month-2026")]
    [InlineData("Month-2026-13")]
    [InlineData("Year")]
    public void Filters_RejectNonsense(string? text) => Assert.False(MemoryFilter.TryParse(text, out _));

    [Fact]
    public void Bubbles_NeverOverlap()
    {
        double[] sizes = [140, 126, 112, 98, 86, 86, 86, 86, 86, 86, 86, 86];
        var spots = BubblePacking.Place(sizes, 240);

        for (var a = 0; a < sizes.Length; a++)
        {
            Assert.InRange(spots[a].Y, 0, 240 - sizes[a]);
            for (var b = a + 1; b < sizes.Length; b++)
            {
                var dx = spots[a].X + sizes[a] / 2 - (spots[b].X + sizes[b] / 2);
                var dy = spots[a].Y + sizes[a] / 2 - (spots[b].Y + sizes[b] / 2);
                Assert.True(Math.Sqrt(dx * dx + dy * dy) >= (sizes[a] + sizes[b]) / 2, $"{a} overlaps {b}");
            }
        }
    }

    [Fact]
    public void ASmallBox_ShowsEveryMemoryOnceAndHasEdges()
    {
        var grid = new MemoryGrid(10);
        Assert.False(grid.Repeats);

        var shown = grid.Visible(-1000, -1000, 4000, 4000).Select(c => c.Index).OrderBy(i => i).ToList();
        Assert.Equal(Enumerable.Range(0, 10), shown);
        Assert.NotEqual(5000, grid.Clamp(5000, 0, 390, 400).X);
    }

    [Fact]
    public void ABigBox_RepeatsFarAway_AndKeepsEachSpotStable()
    {
        var grid = new MemoryGrid(MemoryGrid.RepeatFrom);
        Assert.True(grid.Repeats);
        Assert.Equal(5000, grid.Clamp(5000, 0, 390, 400).X);

        var screen = grid.Visible(0, 0, 390, 450).ToList();
        Assert.Equal(screen.Count, screen.Select(c => c.Index).Distinct().Count());
        Assert.Equal(screen, grid.Visible(0, 0, 390, 450).ToList());

        var far = grid.Visible(-90000, 70000, 390, 450).Select(c => c.Index).ToList();
        Assert.All(far, i => Assert.InRange(i, 0, MemoryGrid.RepeatFrom - 1));
    }

    [Fact]
    public void AnEmptyBox_ShowsNothing() => Assert.Empty(new MemoryGrid(0).Visible(0, 0, 390, 450));
}
