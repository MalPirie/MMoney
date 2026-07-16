using System;
using System.Collections.Generic;
using MMoney.Core.Repeat;

namespace MMoney.App.Repeat;

/// <summary>
/// Pure helpers behind the §8 repeat picker (ADR-0007): the Monthly day-in-month options for an origin, the
/// preset strategies built from an origin, and the lenient number coercion. MauiReactor-free, so they are
/// unit-tested headlessly in <c>MMoney.App.Tests</c>.
/// </summary>
public static class RepeatEditing
{
    /// <summary>
    /// The day-in-month options offered for a Monthly rule anchored at <paramref name="origin"/>: always
    /// <see cref="DayInMonth.DayOfMonth"/>; the nth weekday (<see cref="DayInMonth.First"/>…<see cref="DayInMonth.Fourth"/>)
    /// when the origin is the 1st–4th such weekday; and <see cref="DayInMonth.Last"/> when it is that weekday's
    /// final occurrence in the month.
    /// </summary>
    public static IReadOnlyList<DayInMonth> MonthlyOptions(DateOnly origin)
    {
        var options = new List<DayInMonth> { DayInMonth.DayOfMonth };

        var nth = (origin.Day - 1) / 7 + 1; // 1-based ordinal of this weekday within the month
        if (nth <= 4)
        {
            options.Add((DayInMonth)nth); // First=1 … Fourth=4
        }

        if (origin.Day + 7 > DateTime.DaysInMonth(origin.Year, origin.Month))
        {
            options.Add(DayInMonth.Last); // no later same-weekday this month → it's the last
        }

        return options;
    }

    /// <summary>The default <see cref="RepeatStrategy"/> for each preset, built from the origin (all end Forever).</summary>
    public static RepeatStrategy EveryDay() => new RepeatStrategy.Daily(1);

    public static RepeatStrategy EveryWeek(DateOnly origin) => new RepeatStrategy.Weekly(1, ToDaysOfWeek(origin.DayOfWeek));

    public static RepeatStrategy EveryMonth() => new RepeatStrategy.Monthly(1, DayInMonth.DayOfMonth);

    public static RepeatStrategy EveryYear() => new RepeatStrategy.Yearly(1);

    /// <summary>The single-weekday <see cref="DaysOfWeek"/> flag for a <see cref="DayOfWeek"/> (Monday-based).</summary>
    public static DaysOfWeek ToDaysOfWeek(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => DaysOfWeek.Monday,
        DayOfWeek.Tuesday => DaysOfWeek.Tuesday,
        DayOfWeek.Wednesday => DaysOfWeek.Wednesday,
        DayOfWeek.Thursday => DaysOfWeek.Thursday,
        DayOfWeek.Friday => DaysOfWeek.Friday,
        DayOfWeek.Saturday => DaysOfWeek.Saturday,
        _ => DaysOfWeek.Sunday
    };

    /// <summary>Parse a positive count from lenient user text; empty / invalid / &lt; 1 all coerce to 1 (ADR-0007).</summary>
    public static int CoerceCount(string text) => int.TryParse(text, out var value) && value >= 1 ? value : 1;
}
