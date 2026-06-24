using System.Numerics;

namespace MMoney.Core.Repeat;

/// <summary>
/// Renders a <see cref="RepeatStrategy"/> and <see cref="RepeatEndCondition"/> as human-readable text.
/// Pure presentation — depends on nothing in the ledger. When an end date is needed (for
/// <see cref="RepeatEndCondition.AfterOccurrences"/>), the caller supplies it (see
/// <see cref="RepeatScheduler.EndDate"/>).
/// </summary>
public static class RepeatDescription
{
    /// <summary>Describes how a transaction repeats, e.g. "every month on the 5th".</summary>
    public static string Describe(RepeatStrategy strategy, DateOnly origin) => strategy switch
    {
        RepeatStrategy.Never => "does not repeat",
        RepeatStrategy.Daily { Interval: 1 } => "every day",
        RepeatStrategy.Daily daily => $"every {daily.Interval} days",
        RepeatStrategy.Weekly { Interval: 1, Days: var days } => FormatWeeklyDays(1, days),
        RepeatStrategy.Weekly weekly => FormatWeeklyDays(weekly.Interval, weekly.Days),
        RepeatStrategy.Monthly { Interval: 1, DayInMonth: DayInMonth.DayOfMonth } => $"every month on the {FormatOrdinalDay(origin.Day)}",
        RepeatStrategy.Monthly { Interval: 1, DayInMonth: var dim } => $"every month on the {FormatDayInMonth(dim)} {origin.DayOfWeek}",
        RepeatStrategy.Monthly { DayInMonth: DayInMonth.DayOfMonth } monthly => $"every {monthly.Interval} months on the {FormatOrdinalDay(origin.Day)}",
        RepeatStrategy.Monthly monthly => $"every {monthly.Interval} months on the {FormatDayInMonth(monthly.DayInMonth)} {origin.DayOfWeek}",
        RepeatStrategy.Yearly { Interval: 1 } => $"every year on {origin:d MMMM}",
        RepeatStrategy.Yearly yearly => $"every {yearly.Interval} years on {origin:d MMMM}",
        _ => throw new InvalidOperationException()
    };

    /// <summary>
    /// Describes when repetition ends, e.g. ", until 1 Jan 2027" or ", 12 occurrences (ends ...)".
    /// <paramref name="endDate"/> is the schedule's computed end date (from <see cref="RepeatScheduler.EndDate"/>);
    /// it is only consulted for <see cref="RepeatEndCondition.AfterOccurrences"/>.
    /// </summary>
    public static string DescribeEndCondition(RepeatEndCondition endCondition, DateOnly? endDate) => endCondition switch
    {
        RepeatEndCondition.Forever => string.Empty,
        RepeatEndCondition.UntilDate untilDate => $", until {untilDate.Date:d MMM yyyy}",
        RepeatEndCondition.AfterOccurrences afterOcc =>
            endDate is DateOnly date
                ? $", {afterOcc.Occurrences} occurrences (ends {date:d MMM yyyy})"
                : $", {afterOcc.Occurrences} occurrences",
        _ => string.Empty
    };

    private static string FormatWeeklyDays(int interval, DaysOfWeek days)
    {
        if (interval == 1)
        {
            if (days == DaysOfWeek.All)
            {
                return "every day";
            }

            if (days == DaysOfWeek.Weekdays)
            {
                return "every weekday";
            }

            if (days == DaysOfWeek.Weekend)
            {
                return "every weekend day";
            }

            if (BitOperations.PopCount((ushort)days) == 1)
            {
                return $"every {FormatDays(days)}";
            }
        }

        return interval == 1
            ? $"every week on {FormatDays(days)}"
            : $"every {interval} weeks on {FormatDays(days)}";
    }

    private static string FormatDays(DaysOfWeek days)
    {
        var ordered = new[] {
            DaysOfWeek.Monday, DaysOfWeek.Tuesday, DaysOfWeek.Wednesday,
            DaysOfWeek.Thursday, DaysOfWeek.Friday, DaysOfWeek.Saturday, DaysOfWeek.Sunday
        };
        var names = ordered.Where(d => (days & d) != 0).Select(d => d.ToString()).ToList();
        return names.Count switch
        {
            0 => "",
            1 => names[0],
            _ => string.Join(", ", names[..^1]) + " and " + names[^1]
        };
    }

    private static string FormatOrdinalDay(int day) => day switch
    {
        1 or 21 or 31 => $"{day}st",
        2 or 22 => $"{day}nd",
        3 or 23 => $"{day}rd",
        _ => $"{day}th"
    };

    private static string FormatDayInMonth(DayInMonth dayInMonth) => dayInMonth switch
    {
        DayInMonth.First => "first",
        DayInMonth.Second => "second",
        DayInMonth.Third => "third",
        DayInMonth.Fourth => "fourth",
        DayInMonth.Last => "last",
        _ => throw new InvalidOperationException()
    };
}
