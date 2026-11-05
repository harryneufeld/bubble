namespace Bubble.Core;

/// <summary>Highlights of one calendar month, newest first.</summary>
public sealed record MonthGroup<T>(int Year, int Month, IReadOnlyList<T> Items);

/// <summary>Highlights from this calendar day some time ago: a year (or more), or else a month.</summary>
public sealed record Recall<T>(int MonthsAgo, IReadOnlyList<T> Items);

/// <summary>Calendar logic for the Today screen and Rewind. Everything is counted in the local calendar of <c>now</c>.</summary>
public static class Timeline
{
    /// <summary>Monday, 00:00 of the week <paramref name="now"/> is in.</summary>
    public static DateTimeOffset WeekStart(DateTimeOffset now) =>
        new DateTimeOffset(now.Date, now.Offset).AddDays(-(((int)now.DayOfWeek + 6) % 7));

    public static DateTime LocalDate(DateTimeOffset createdAt, DateTimeOffset now) => createdAt.ToOffset(now.Offset).Date;

    /// <summary>Months that have highlights, newest first.</summary>
    public static IReadOnlyList<MonthGroup<T>> ByMonth<T>(IEnumerable<T> items, Func<T, DateTimeOffset> createdAt, DateTimeOffset now) =>
        items.OrderByDescending(createdAt)
            .GroupBy(i => LocalDate(createdAt(i), now) is var d ? (d.Year, d.Month) : default)
            .Select(g => new MonthGroup<T>(g.Key.Year, g.Key.Month, g.ToList()))
            .ToList();

    /// <summary>The closest anniversary that has highlights: 1 to 10 years ago, else one month ago. Null if there is none.</summary>
    public static Recall<T>? OnThisDay<T>(IEnumerable<T> items, Func<T, DateTimeOffset> createdAt, DateTimeOffset now)
    {
        var dated = items.Select(i => (Item: i, Date: LocalDate(createdAt(i), now))).ToList();
        foreach (var months in Enumerable.Range(1, 10).Select(y => y * 12).Append(1))
        {
            var day = now.Date.AddMonths(-months);
            var hits = dated.Where(x => x.Date == day).Select(x => x.Item).ToList();
            if (hits.Count > 0) return new Recall<T>(months, hits);
        }
        return null;
    }

    /// <summary>The last <paramref name="count"/> months up to and including this one, oldest first.</summary>
    public static IReadOnlyList<(int Year, int Month)> LastMonths(DateTimeOffset now, int count) =>
        Enumerable.Range(0, count).Reverse().Select(i => now.Date.AddMonths(-i)).Select(d => (d.Year, d.Month)).ToList();
}
