namespace MMoney.Core.Repeat;

/// <summary>
/// A set of weekdays as bit flags, used by <see cref="RepeatStrategy.Weekly"/>. Monday is bit 0
/// (note: this differs from <see cref="System.DayOfWeek"/>, where Sunday is 0).
/// </summary>
[Flags]
public enum DaysOfWeek : short
{
    None = 0,
    Monday = 1 << 0,
    Tuesday = 1 << 1,
    Wednesday = 1 << 2,
    Thursday = 1 << 3,
    Friday = 1 << 4,
    Saturday = 1 << 5,
    Sunday = 1 << 6,

    Weekdays = Monday | Tuesday | Wednesday | Thursday | Friday,
    Weekend = Saturday | Sunday,
    All = Weekdays | Weekend
}