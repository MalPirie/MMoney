using System;
using System.Collections.Generic;

namespace Mobiorum.Material3;

/// <summary>
/// One cell of a month grid: a <see cref="Date"/> (null for the leading blanks that pad the first row so day 1
/// sits under its weekday), and whether it is <see cref="Enabled"/> (a real day on or after the minimum).
/// </summary>
public readonly record struct CalendarCell(DateOnly? Date, bool Enabled);

/// <summary>
/// The pure month-grid layout behind the <see cref="Calendar"/> control: given a year/month (and an optional
/// minimum selectable date), it produces the ordered cells for a <b>Monday-start</b> grid — leading blanks to
/// align the 1st under its weekday, then each day of the month, each flagged enabled/disabled against the
/// minimum. MauiReactor- and Core-free (BCL <see cref="DateOnly"/> only), so it is unit-tested headlessly in
/// <c>Mobiorum.Material3.Tests</c> and the library keeps its zero dependency on the app.
/// </summary>
public static class CalendarGrid
{
    /// <summary>Cells to render for the given month, Monday-start, disabling any day before <paramref name="minDate"/>.</summary>
    public static IReadOnlyList<CalendarCell> ForMonth(int year, int month, DateOnly? minDate)
    {
        var cells = new List<CalendarCell>();

        var first = new DateOnly(year, month, 1);
        for (var blank = 0; blank < LeadingBlanks(first); blank++)
        {
            cells.Add(new CalendarCell(null, false));
        }

        var days = DateTime.DaysInMonth(year, month);
        for (var day = 1; day <= days; day++)
        {
            var date = new DateOnly(year, month, day);
            cells.Add(new CalendarCell(date, minDate is null || date >= minDate.Value));
        }

        return cells;
    }

    /// <summary>Whether the month before <paramref name="year"/>/<paramref name="month"/> holds any selectable day
    /// (i.e. the prev-month arrow should be enabled): true unless <paramref name="minDate"/> falls in this month
    /// or later.</summary>
    public static bool HasPreviousMonth(int year, int month, DateOnly? minDate) =>
        minDate is null || minDate.Value < new DateOnly(year, month, 1);

    /// <summary>The Monday-start column (0 = Monday … 6 = Sunday) the given date sits in.</summary>
    public static int ColumnOf(DateOnly date) => ((int)date.DayOfWeek + 6) % 7;

    // Blanks before day 1 = its Monday-start column index.
    private static int LeadingBlanks(DateOnly firstOfMonth) => ColumnOf(firstOfMonth);
}
