namespace MMoney.Core.Repeat;

/// <summary>
/// Turns a <see cref="RepeatStrategy"/>, a <see cref="RepeatEndCondition"/> and an origin date into the
/// concrete dates an occurrence falls on. The single home of all repeat date maths; it carries no
/// presentation concerns. <see cref="RepeatStrategy"/> stays pure data — the per-strategy generators live
/// here, behind this small interface.
/// </summary>
public sealed class RepeatScheduler
{
    /// <summary>
    /// The dates on which the schedule produces an occurrence within <paramref name="month"/>,
    /// clamped to the month and to the schedule's end date.
    /// </summary>
    public IEnumerable<DateOnly> DatesForMonth(
        RepeatStrategy strategy, RepeatEndCondition endCondition, DateOnly origin, MonthOnly month)
    {
        var endDate = EndDate(strategy, endCondition, origin);
        var monthEnd = month.LastDay;
        var effectiveEnd = endDate < monthEnd ? endDate : monthEnd;
        var effectiveStart = month.FirstDay > origin ? month.FirstDay : origin;

        return Dates(strategy, origin, effectiveStart).TakeWhile(date => date <= effectiveEnd);
    }

    /// <summary>
    /// The first date on or after <paramref name="from"/> on which the schedule produces an occurrence,
    /// or <see langword="null"/> when the schedule has already ended by then.
    /// </summary>
    public DateOnly? NextOnOrAfter(
        RepeatStrategy strategy, RepeatEndCondition endCondition, DateOnly origin, DateOnly from)
    {
        var endDate = EndDate(strategy, endCondition, origin);
        var start = from > origin ? from : origin;
        if (start > endDate)
        {
            return null;
        }

        return Dates(strategy, origin, start)
            .TakeWhile(date => date <= endDate)
            .Cast<DateOnly?>()
            .FirstOrDefault();
    }

    /// <summary>
    /// The final date the schedule produces an occurrence, or <see cref="DateOnly.MaxValue"/> when it
    /// repeats forever.
    /// </summary>
    public DateOnly EndDate(RepeatStrategy strategy, RepeatEndCondition endCondition, DateOnly origin) => endCondition switch
    {
        RepeatEndCondition.Forever => DateOnly.MaxValue,
        RepeatEndCondition.UntilDate untilDate => untilDate.Date,
        RepeatEndCondition.AfterOccurrences afterOcc =>
            Dates(strategy, origin, origin).Skip(afterOcc.Occurrences - 1).FirstOrDefault(DateOnly.MaxValue),
        _ => throw new InvalidOperationException()
    };

    // ---- Per-strategy date generation ------------------------------------------------------------
    // Each generator yields an infinite, ascending, lazy sequence of occurrence dates on or after
    // `start`; the public methods above clamp it to the relevant window.

    private static IEnumerable<DateOnly> Dates(RepeatStrategy strategy, DateOnly origin, DateOnly start) => strategy switch
    {
        RepeatStrategy.Never => origin == start ? [origin] : [],
        RepeatStrategy.Daily daily => DailyDates(daily, origin, start),
        RepeatStrategy.Weekly weekly => WeeklyDates(weekly, origin, start),
        RepeatStrategy.Monthly monthly => MonthlyDates(monthly, origin, start),
        RepeatStrategy.Yearly yearly => YearlyDates(yearly, origin, start),
        _ => throw new InvalidOperationException()
    };

    private static IEnumerable<DateOnly> DailyDates(RepeatStrategy.Daily configuration, DateOnly origin, DateOnly start)
    {
        ThrowIfOriginAfterStart(origin, start);

        var difference = start.DayNumber - origin.DayNumber;
        var offset = difference >= 0 ? (difference + configuration.Interval - 1) / configuration.Interval : 0;
        var date = origin.AddDays(offset * configuration.Interval);
        while (true)
        {
            yield return date;
            date = date.AddDays(configuration.Interval);
        }
    }

    private static IEnumerable<DateOnly> WeeklyDates(RepeatStrategy.Weekly configuration, DateOnly origin, DateOnly start)
    {
        ThrowIfOriginAfterStart(origin, start);

        var daysBetween = start.DayNumber - origin.DayNumber;
        var weeksBetween = daysBetween >= 0 ? daysBetween / 7 : (daysBetween - 6) / 7;
        var intervalIndex = Math.Max(0, weeksBetween / configuration.Interval);

        while (true)
        {
            var weekStart = origin.AddDays(intervalIndex * configuration.Interval * 7);

            for (var i = 0; i < 7; i++)
            {
                var date = weekStart.AddDays(i);
                if (date >= start && (configuration.Days & ToMask(date.DayOfWeek)) != 0)
                {
                    yield return date;
                }
            }

            intervalIndex++;
        }
    }

    private static IEnumerable<DateOnly> MonthlyDates(RepeatStrategy.Monthly configuration, DateOnly origin, DateOnly start)
    {
        ThrowIfOriginAfterStart(origin, start);

        var difference = (start.Year - origin.Year) * 12 + (start.Month - origin.Month);
        var offset = difference >= 0 ? (difference + configuration.Interval - 1) / configuration.Interval : 0;

        var originDow = origin.DayOfWeek;
        var originDay = origin.Day;

        while (true)
        {
            var month = origin.AddMonths(offset * configuration.Interval);
            var date = ResolveMonthDate(month, originDow, originDay, configuration.DayInMonth);

            if (date >= start)
            {
                yield return date;
            }

            offset++;
        }
    }

    private static IEnumerable<DateOnly> YearlyDates(RepeatStrategy.Yearly configuration, DateOnly origin, DateOnly start)
    {
        ThrowIfOriginAfterStart(origin, start);

        // Legacy logs serialized Yearly without an interval; treat a missing/invalid value as every year.
        var interval = configuration.Interval > 0 ? configuration.Interval : 1;

        // Always measure from the origin, never cumulatively from the previous occurrence: AddYears clamps
        // 29 Feb to 28 Feb in non-leap years but restores the 29th in leap years when computed from the origin.
        var yearsFromOrigin = Math.Max(0, start.Year - origin.Year - 1);
        var offset = yearsFromOrigin / interval * interval;
        while (true)
        {
            var date = origin.AddYears(offset);
            if (date >= start)
            {
                yield return date;
            }

            offset += interval;
        }
    }

    private static DateOnly ResolveMonthDate(DateOnly monthDate, DayOfWeek originDow, int originDay, DayInMonth rule)
    {
        var year = monthDate.Year;
        var month = monthDate.Month;
        var daysInMonth = DateTime.DaysInMonth(year, month);

        if (rule == DayInMonth.DayOfMonth)
        {
            return new DateOnly(year, month, Math.Min(originDay, daysInMonth));
        }

        var firstOfMonth = new DateOnly(year, month, 1);
        var delta = ((int)originDow - (int)firstOfMonth.DayOfWeek + 7) % 7;
        var firstOccurrenceDay = 1 + delta;

        if (rule == DayInMonth.Last)
        {
            var lastOfMonth = new DateOnly(year, month, daysInMonth);
            var back = ((int)lastOfMonth.DayOfWeek - (int)originDow + 7) % 7;
            return lastOfMonth.AddDays(-back);
        }

        var n = (int)rule;
        var day = firstOccurrenceDay + (n - 1) * 7;

        if (day > daysInMonth)
        {
            throw new InvalidOperationException($"Month {year}-{month:D2} does not contain {rule} {originDow}");
        }

        return new DateOnly(year, month, day);
    }

    // System.DayOfWeek is Sunday=0..Saturday=6; the DaysOfWeek flags are Monday=bit0..Sunday=bit6.
    // Rotate so Monday maps to bit 0.
    private static DaysOfWeek ToMask(DayOfWeek dayOfWeek) => (DaysOfWeek)(1 << (((int)dayOfWeek + 6) % 7));

    private static void ThrowIfOriginAfterStart(DateOnly origin, DateOnly start)
    {
        if (origin > start)
        {
            throw new InvalidOperationException("Origin date is after start date");
        }
    }
}
