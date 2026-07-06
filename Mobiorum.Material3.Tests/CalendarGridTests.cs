using System;
using System.Linq;
using Mobiorum.Material3;
using Xunit;

namespace Mobiorum.Material3.Tests;

/// <summary>The pure Monday-start month-grid layout behind the Calendar control (ADR-0006).</summary>
public class CalendarGridTests
{
    // ── leading blanks / alignment ────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ColumnOf_IsMondayZeroThroughSundaySix()
    {
        Assert.Equal(0, CalendarGrid.ColumnOf(new DateOnly(2026, 7, 6)));  // Monday
        Assert.Equal(6, CalendarGrid.ColumnOf(new DateOnly(2026, 7, 5)));  // Sunday
    }

    [Fact]
    public void ForMonth_PadsLeadingBlanksToAlignTheFirst()
    {
        // 1 July 2026 is a Wednesday → Monday-start column 2 → two leading blanks.
        var cells = CalendarGrid.ForMonth(2026, 7, null);

        Assert.Equal(2, cells.TakeWhile(c => c.Date is null).Count());
        Assert.Equal(new DateOnly(2026, 7, 1), cells[2].Date);
    }

    [Fact]
    public void ForMonth_MonthStartingMonday_HasNoLeadingBlanks()
    {
        // 1 June 2026 is a Monday.
        var cells = CalendarGrid.ForMonth(2026, 6, null);
        Assert.Equal(new DateOnly(2026, 6, 1), cells[0].Date);
    }

    [Fact]
    public void ForMonth_EmitsEveryDayOfTheMonth()
    {
        var cells = CalendarGrid.ForMonth(2026, 2, null); // Feb 2026 = 28 days
        var days = cells.Where(c => c.Date is not null).Select(c => c.Date!.Value.Day).ToArray();
        Assert.Equal(Enumerable.Range(1, 28).ToArray(), days);
    }

    [Fact]
    public void ForMonth_LongMonthStartingSunday_SpansSixRows()
    {
        // 1 Aug 2026 is a Saturday (column 5) → 5 blanks + 31 days = 36 cells → 6 rows of 7 (with padding).
        var cells = CalendarGrid.ForMonth(2026, 8, null);
        var rows = (int)Math.Ceiling(cells.Count / 7.0);
        Assert.Equal(6, rows);
    }

    // ── min-date disabling ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ForMonth_DisablesDaysBeforeTheMinimum()
    {
        var cells = CalendarGrid.ForMonth(2026, 7, new DateOnly(2026, 7, 10));

        Assert.False(Cell(cells, 2026, 7, 9).Enabled);
        Assert.True(Cell(cells, 2026, 7, 10).Enabled);  // the minimum itself is selectable
        Assert.True(Cell(cells, 2026, 7, 11).Enabled);
    }

    [Fact]
    public void ForMonth_WithNoMinimum_EnablesEveryDay()
    {
        var cells = CalendarGrid.ForMonth(2026, 7, null);
        Assert.All(cells.Where(c => c.Date is not null), c => Assert.True(c.Enabled));
    }

    // ── previous-month availability ───────────────────────────────────────────────────────────────────────

    [Fact]
    public void HasPreviousMonth_TrueWhenUnbounded()
    {
        Assert.True(CalendarGrid.HasPreviousMonth(2026, 7, null));
    }

    [Fact]
    public void HasPreviousMonth_FalseWhenMinimumIsInThisMonth()
    {
        // The lock sits inside July → nothing selectable before July.
        Assert.False(CalendarGrid.HasPreviousMonth(2026, 7, new DateOnly(2026, 7, 20)));
    }

    [Fact]
    public void HasPreviousMonth_TrueWhenMinimumIsInAnEarlierMonth()
    {
        Assert.True(CalendarGrid.HasPreviousMonth(2026, 7, new DateOnly(2026, 6, 30)));
    }

    private static CalendarCell Cell(System.Collections.Generic.IReadOnlyList<CalendarCell> cells, int y, int m, int d) =>
        cells.Single(c => c.Date == new DateOnly(y, m, d));
}
