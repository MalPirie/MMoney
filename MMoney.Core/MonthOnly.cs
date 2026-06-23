using System.Text.Json.Serialization;

namespace MMoney.Core;

/// <summary>Represents a calendar month without a day component.</summary>
[JsonConverter(typeof(MonthOnlyJsonConverter))]
public readonly record struct MonthOnly(int Year, int Month) : IComparable<MonthOnly>
{
    public static readonly MonthOnly Undefined = new();
    public static readonly MonthOnly MaxValue = new(int.MaxValue, 12);

    /// <summary>The calendar year.</summary>
    public int Year { get; } = Year;

    /// <summary>The month of the year (1–12).</summary>
    public int Month { get; } = Month;

    /// <summary>The first day of the month.</summary>
    public DateOnly FirstDay => this != Undefined 
        ? new(Year, Month, 1) 
        : throw new InvalidOperationException();

    /// <summary>The last day of the month, accounting for month length and leap years.</summary>
    public DateOnly LastDay => this != Undefined 
        ? new(Year, Month, DateTime.DaysInMonth(Year, Month)) 
        : throw new InvalidOperationException();

    /// <summary>Compares this month to another, ordering by year then month.</summary>
    public int CompareTo(MonthOnly other) =>
        Year != other.Year
            ? Year.CompareTo(other.Year)
            : Month.CompareTo(other.Month);

    /// <summary>Returns a new <see cref="MonthOnly"/> offset by the given number of months.</summary>
    /// <param name="months">The number of months to add. May be negative.</param>
    public MonthOnly Add(int months)
    {
        if (this == Undefined)
        {
            throw new InvalidOperationException();
        }

        var totalMonths = Year * 12 + Month - 1 + months;
        var newYear = totalMonths / 12;
        var newMonth = totalMonths % 12 + 1;
        return new MonthOnly(newYear, newMonth);
    }

    /// <summary>
    /// The inclusive run of months from this month up to and including <paramref name="end"/>, in
    /// ascending order. Empty when <paramref name="end"/> is before this month.
    /// </summary>
    public IEnumerable<MonthOnly> To(MonthOnly end)
    {
        if (this == Undefined)
        {
            throw new InvalidOperationException();
        }

        for (var month = this; month.CompareTo(end) <= 0; month = month.Add(1))
        {
            yield return month;
        }
    }

    /// <summary>Returns the month in <c>yyyy-MM</c> format.</summary>
    public override string ToString() => this != Undefined ? $"{Year:D4}-{Month:D2}" : "(Undefined)";

    /// <summary>Creates a <see cref="MonthOnly"/> from the year and month of the given date.</summary>
    public static MonthOnly FromDate(DateOnly date) => new(date.Year, date.Month);
}