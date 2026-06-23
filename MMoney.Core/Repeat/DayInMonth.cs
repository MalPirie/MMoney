namespace MMoney.Core.Repeat;

/// <summary>
/// Which day a <see cref="RepeatStrategy.Monthly"/> occurrence lands on each month.
/// </summary>
/// <remarks>
/// The <see cref="First"/>–<see cref="Fourth"/> values are 1–4 deliberately: the scheduler uses them directly
/// as the nth-week index when resolving the origin's weekday within a month.
/// </remarks>
public enum DayInMonth
{
    /// <summary>The origin's day-of-month, clamped to the month's length (e.g. the 31st becomes the 28th in February).</summary>
    DayOfMonth = 0,

    /// <summary>The first occurrence of the origin's weekday in the month.</summary>
    First = 1,

    /// <summary>The second occurrence of the origin's weekday in the month.</summary>
    Second = 2,

    /// <summary>The third occurrence of the origin's weekday in the month.</summary>
    Third = 3,

    /// <summary>The fourth occurrence of the origin's weekday in the month.</summary>
    Fourth = 4,

    /// <summary>The last occurrence of the origin's weekday in the month.</summary>
    Last = 9
}