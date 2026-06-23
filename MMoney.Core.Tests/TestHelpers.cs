using MMoney.Core;
using MMoney.Core.Repeat;

namespace MMoney.Core.Tests;

internal static class TestHelpers
{
    public static DateOnly D(int year, int month, int day) => new(year, month, day);

    public static MonthOnly M(int year, int month) => new(year, month);

    public static Account NewAccount() => new(Guid.NewGuid(), []);

    public static RepeatStrategy MonthlyOnDay() => new RepeatStrategy.Monthly(1, DayInMonth.DayOfMonth);

    public static RepeatEndCondition Forever() => new RepeatEndCondition.Forever();

    /// <summary>Builds an account while capturing the events it emits, for replay-based assertions.</summary>
    public static (Account Account, List<AccountEvent> Events) Recording()
    {
        var events = new List<AccountEvent>();
        var account = new Account(Guid.NewGuid(), []);
        account.NewEvent += (_, e) => events.Add(e);
        return (account, events);
    }

    /// <summary>A clock fixed at 2026-06-23 (UTC, treated as local) for deterministic manager tests.</summary>
    public static TimeProvider Clock() => new FixedClock(new DateTimeOffset(2026, 6, 23, 12, 0, 0, TimeSpan.Zero));
}

/// <summary>A <see cref="TimeProvider"/> frozen at a given instant, with local time pinned to UTC.</summary>
internal sealed class FixedClock(DateTimeOffset utcNow) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => utcNow;

    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
}
