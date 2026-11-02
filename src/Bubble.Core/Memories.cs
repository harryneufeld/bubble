namespace Bubble.Core;

/// <summary>Where a kept highlight belongs when looking back. Every highlight is in exactly one period.</summary>
public enum MemoryPeriod { Week, Month, Year, Older }

public static class MemoryPeriods
{
    /// <summary>Monday, 00:00 of the week <paramref name="now"/> is in.</summary>
    public static DateTimeOffset WeekStart(DateTimeOffset now) =>
        new DateTimeOffset(now.Date, now.Offset).AddDays(-(((int)now.DayOfWeek + 6) % 7));

    /// <summary>This week (from Monday), then the rest of this month, the rest of this year, everything before.</summary>
    public static MemoryPeriod Of(DateTimeOffset createdAt, DateTimeOffset now)
    {
        if (createdAt >= WeekStart(now)) return MemoryPeriod.Week;
        var local = createdAt.ToOffset(now.Offset);
        if (local.Year != now.Year) return MemoryPeriod.Older;
        return local.Month == now.Month ? MemoryPeriod.Month : MemoryPeriod.Year;
    }
}

/// <summary>What the box of memories shows: this week, one month, or one year. Unlike a period, these overlap: a month contains its weeks.</summary>
public enum MemoryRange { Week, Month, Year }

public readonly record struct MemoryFilter(MemoryRange Range, int Year = 0, int Month = 0)
{
    public static readonly MemoryFilter ThisWeek = new(MemoryRange.Week);

    public static MemoryFilter OfMonth(int year, int month) => new(MemoryRange.Month, year, month);

    public static MemoryFilter OfYear(int year) => new(MemoryRange.Year, year);

    public bool Matches(DateTimeOffset createdAt, DateTimeOffset now)
    {
        var local = createdAt.ToOffset(now.Offset);
        return Range switch
        {
            MemoryRange.Week => createdAt >= MemoryPeriods.WeekStart(now),
            MemoryRange.Month => local.Year == Year && local.Month == Month,
            _ => local.Year == Year,
        };
    }

    /// <summary>"Week", "Month-2026-10", "Year-2026": for passing a filter to another page.</summary>
    public string Encode() => Range switch
    {
        MemoryRange.Week => "Week",
        MemoryRange.Month => $"Month-{Year}-{Month}",
        _ => $"Year-{Year}",
    };

    public static bool TryParse(string? text, out MemoryFilter filter)
    {
        filter = ThisWeek;
        var parts = (text ?? "").Split('-');
        var year = parts.Length > 1 && int.TryParse(parts[1], out var y) ? y : 0;
        var month = parts.Length > 2 && int.TryParse(parts[2], out var m) ? m : 0;
        switch (parts[0])
        {
            case "Week" when parts.Length == 1:
                return true;
            case "Month" when year > 0 && month is >= 1 and <= 12:
                filter = OfMonth(year, month);
                return true;
            case "Year" when year > 0:
                filter = OfYear(year);
                return true;
            default:
                return false;
        }
    }
}

/// <summary>Packs circles of different sizes into a band of fixed height, from left to right, without overlaps.</summary>
public static class BubblePacking
{
    const double Step = 4;

    /// <summary>Top-left corner of each circle. The order of the sizes is the order from left to right.</summary>
    public static IReadOnlyList<(double X, double Y)> Place(IReadOnlyList<double> diameters, double height, double gap = 6)
    {
        var placed = new List<(double X, double Y)>();
        for (var i = 0; i < diameters.Count; i++)
        {
            var d = diameters[i];
            var room = Math.Max(0, height - d);
            var spot = (X: 0.0, Y: 0.0);
            var found = false;
            for (double x = 0; !found; x += Step)
            {
                // alternate between starting from the top and from the bottom, so they don't form a straight row
                for (double offset = 0; offset <= room && !found; offset += Step)
                {
                    var y = i % 2 == 0 ? offset : room - offset;
                    if (!Fits(x, y, d, i, diameters, placed, gap)) continue;
                    spot = (x, y);
                    found = true;
                }
            }
            placed.Add(spot);
        }
        return placed;
    }

    static bool Fits(double x, double y, double d, int count, IReadOnlyList<double> diameters, List<(double X, double Y)> placed, double gap)
    {
        for (var i = 0; i < count; i++)
        {
            var dx = x + d / 2 - (placed[i].X + diameters[i] / 2);
            var dy = y + d / 2 - (placed[i].Y + diameters[i] / 2);
            var min = (d + diameters[i]) / 2 + gap;
            if (dx * dx + dy * dy < min * min) return false;
        }
        return true;
    }
}

/// <summary>A print on the table: its grid cell, which memory it shows, and where it lies.</summary>
public readonly record struct MemoryCell(int Column, int Row, int Index, double Left, double Top, double Rotation, int Layer);

/// <summary>
/// Lays memories out as a loose pile on an endless table. Each memory has a fixed spot, so panning away and back
/// finds the same print. A small box has edges; only from <see cref="RepeatFrom"/> memories on does the table wrap
/// around, and then a repeat is always several screens away.
/// </summary>
public sealed class MemoryGrid
{
    public const int RepeatFrom = 48;
    public const double PrintWidth = 128, PrintHeight = 150;
    const double CellWidth = 136, CellHeight = 152;

    readonly int _count, _columns, _rows;

    public MemoryGrid(int count)
    {
        _count = count;
        _columns = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(count)));
        _rows = (int)Math.Ceiling(count / (double)_columns);
    }

    public bool Repeats => _count >= RepeatFrom;

    /// <summary>All prints that touch the viewport at this pan position.</summary>
    public IEnumerable<MemoryCell> Visible(double panX, double panY, double viewWidth, double viewHeight)
    {
        if (_count == 0) yield break;
        var firstRow = (int)Math.Floor(panY / CellHeight) - 1;
        var lastRow = (int)Math.Floor((panY + viewHeight) / CellHeight) + 1;
        var firstColumn = (int)Math.Floor(panX / CellWidth) - 2;
        var lastColumn = (int)Math.Floor((panX + viewWidth) / CellWidth) + 1;

        for (var row = firstRow; row <= lastRow; row++)
            for (var column = firstColumn; column <= lastColumn; column++)
                if (IndexAt(column, row) is { } index)
                    yield return new MemoryCell(column, row, index,
                        column * CellWidth + (row & 1) * CellWidth / 2 + (Noise(column, row, 1) - 0.5) * 30,
                        row * CellHeight + (Noise(column, row, 2) - 0.5) * 26,
                        (Noise(column, row, 3) - 0.5) * 24,
                        (int)(Noise(column, row, 4) * 800) + 1);
    }

    /// <summary>Keeps a small box in view: it stays centered if it fits, otherwise it stops at its edges.</summary>
    public (double X, double Y) Clamp(double panX, double panY, double viewWidth, double viewHeight) => Repeats
        ? (panX, panY)
        : (ClampAxis(panX, _columns * CellWidth + CellWidth / 2, viewWidth), ClampAxis(panY, _rows * CellHeight, viewHeight));

    static double ClampAxis(double pan, double content, double view) =>
        content <= view ? (content - view) / 2 : Math.Clamp(pan, -16, content - view + 16);

    int? IndexAt(int column, int row)
    {
        if (Repeats) return (int)(((column + (long)row * _columns) % _count + _count) % _count);
        if (column < 0 || column >= _columns || row < 0) return null;
        var index = row * _columns + column;
        return index < _count ? index : null;
    }

    // A fixed scatter per cell: the same print always lies the same way
    static double Noise(int column, int row, int salt)
    {
        unchecked
        {
            var h = (uint)(column * 73856093) ^ (uint)(row * 19349663) ^ (uint)(salt * 83492791);
            h ^= h >> 13;
            h *= 0x5bd1e995;
            h ^= h >> 15;
            return h / (double)uint.MaxValue;
        }
    }
}
