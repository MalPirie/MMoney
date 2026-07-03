using System.Text.Json.Serialization;

namespace MMoney.Core.Repeat;

/// <summary>
/// How a transaction recurs. A behaviour-free discriminated union: the date maths live on
/// <see cref="Schedule"/>, and a strategy is serialized inside <see cref="AccountEvent.TransactionAdded"/>
/// using the <c>$repeat</c> discriminator.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$repeat")]
[JsonDerivedType(typeof(Never), "never")]
[JsonDerivedType(typeof(Daily), "daily")]
[JsonDerivedType(typeof(Weekly), "weekly")]
[JsonDerivedType(typeof(Monthly), "monthly")]
[JsonDerivedType(typeof(Yearly), "yearly")]
public abstract record RepeatStrategy
{
    private RepeatStrategy() { }

    /// <summary>Does not recur — a single occurrence at the origin.</summary>
    public sealed record Never() : RepeatStrategy;

    /// <summary>Every <paramref name="Interval"/> days from the origin.</summary>
    /// <param name="Interval">Number of days between occurrences.</param>
    public sealed record Daily(int Interval) : RepeatStrategy;

    /// <summary>On the selected <paramref name="Days"/>, repeating every <paramref name="Interval"/> weeks.</summary>
    /// <param name="Interval">Number of weeks between active weeks (1 = every week).</param>
    /// <param name="Days">The weekdays an occurrence falls on within an active week.</param>
    public sealed record Weekly(int Interval, DaysOfWeek Days) : RepeatStrategy;

    /// <summary>Every <paramref name="Interval"/> months, on the day chosen by <paramref name="DayInMonth"/>.</summary>
    /// <param name="Interval">Number of months between occurrences (1 = every month).</param>
    /// <param name="DayInMonth">Whether the occurrence lands on a day-of-month or an nth/last weekday.</param>
    public sealed record Monthly(int Interval, DayInMonth DayInMonth) : RepeatStrategy;

    /// <summary>Every <paramref name="Interval"/> years on the origin's month and day.</summary>
    /// <param name="Interval">
    /// Years between occurrences (1 = every year). Legacy logs serialized <c>Yearly</c> without this field;
    /// it defaults to 1 there and is treated as 1 if ever missing or non-positive.
    /// </param>
    public sealed record Yearly(int Interval = 1) : RepeatStrategy;
}