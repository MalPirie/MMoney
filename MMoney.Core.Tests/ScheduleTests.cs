using MMoney.Core;
using MMoney.Core.Repeat;
using Xunit;
using static MMoney.Core.Tests.TestHelpers;

namespace MMoney.Core.Tests;

public class ScheduleTests
{
    private static DateOnly[] Dates(IEnumerable<LedgerEntry> rows) => [.. rows.Select(r => r.Date)];

    // ---- Daily -----------------------------------------------------------------------------------

    [Fact]
    public void Daily_EveryDay_OccursOnEveryDayOfTheMonth()
    {
        var account = NewAccount();
        account.AddTransaction(D(2026, 6, 1), 5m, "coffee", new RepeatStrategy.Daily(1), Forever());

        var june = account.GetMonth(M(2026, 6));

        Assert.Equal(30, june.Count);
        Assert.All(june, r => Assert.Equal(LedgerEntryKind.Occurrence, r.Kind));
        Assert.Equal(50m, account.BalanceOn(D(2026, 6, 10)));  // 10 days
        Assert.Equal(150m, account.BalanceOn(D(2026, 6, 30))); // 30 days
    }

    [Fact]
    public void Daily_EveryOtherDay_OccursOnAlternateDaysFromOrigin()
    {
        var account = NewAccount();
        account.AddTransaction(D(2026, 6, 1), 5m, "x", new RepeatStrategy.Daily(2), Forever());

        var dates = Dates(account.GetMonth(M(2026, 6)));

        // 1, 3, 5, ... 29
        Assert.Equal(15, dates.Length);
        Assert.Equal(D(2026, 6, 1), dates[0]);
        Assert.Equal(D(2026, 6, 29), dates[^1]);
        Assert.All(dates, d => Assert.Equal(1, d.Day % 2)); // odd days only
    }

    // ---- Weekly ----------------------------------------------------------------------------------
    // 2026-06-01 is a Monday.

    [Fact]
    public void Weekly_SingleDay_OccursOnThatWeekday()
    {
        var account = NewAccount();
        account.AddTransaction(D(2026, 6, 1), 5m, "gym", new RepeatStrategy.Weekly(1, DaysOfWeek.Monday), Forever());

        var dates = Dates(account.GetMonth(M(2026, 6)));

        Assert.Equal(new[] { D(2026, 6, 1), D(2026, 6, 8), D(2026, 6, 15), D(2026, 6, 22), D(2026, 6, 29) }, dates);
        Assert.Equal(25m, account.BalanceOn(D(2026, 6, 29)));
        Assert.Equal(15m, account.BalanceOn(D(2026, 6, 15)));
    }

    [Fact]
    public void Weekly_MultipleDays_OccursOnEachSelectedDay()
    {
        var account = NewAccount();
        var days = DaysOfWeek.Monday | DaysOfWeek.Wednesday | DaysOfWeek.Friday;
        account.AddTransaction(D(2026, 6, 1), 1m, "class", new RepeatStrategy.Weekly(1, days), Forever());

        var dates = Dates(account.GetMonth(M(2026, 6)));

        Assert.Equal(
            new[]
            {
                D(2026, 6, 1), D(2026, 6, 3), D(2026, 6, 5),
                D(2026, 6, 8), D(2026, 6, 10), D(2026, 6, 12),
                D(2026, 6, 15), D(2026, 6, 17), D(2026, 6, 19),
                D(2026, 6, 22), D(2026, 6, 24), D(2026, 6, 26),
                D(2026, 6, 29),
            },
            dates);
    }

    [Fact]
    public void Weekly_EveryOtherWeek_SkipsTheInterveningWeeks()
    {
        var account = NewAccount();
        account.AddTransaction(D(2026, 6, 1), 5m, "x", new RepeatStrategy.Weekly(2, DaysOfWeek.Monday), Forever());

        var dates = Dates(account.GetMonth(M(2026, 6)));

        Assert.Equal(new[] { D(2026, 6, 1), D(2026, 6, 15), D(2026, 6, 29) }, dates);
    }

    // ---- Yearly ----------------------------------------------------------------------------------

    [Fact]
    public void Yearly_OccursOnceAYearOnTheOriginDate()
    {
        var account = NewAccount();
        account.AddTransaction(D(2025, 3, 10), 100m, "insurance", new RepeatStrategy.Yearly(), Forever());

        Assert.Equal(new[] { D(2025, 3, 10) }, Dates(account.GetMonth(M(2025, 3))));
        Assert.Equal(new[] { D(2026, 3, 10) }, Dates(account.GetMonth(M(2026, 3))));
        Assert.Empty(account.GetMonth(M(2026, 4)));
        Assert.Equal(300m, account.BalanceOn(D(2027, 3, 10))); // 2025, 2026, 2027
    }

    [Fact]
    public void Yearly_LeapDayOrigin_ClampsToFeb28ButRestoresFeb29InLeapYears()
    {
        // A 29 Feb origin clamps to 28 Feb in non-leap years and returns to the 29th in leap years, because
        // every occurrence is measured from the origin rather than stepped from the previous (clamped) date.
        var account = NewAccount();
        account.AddTransaction(D(2024, 2, 29), 50m, "leap", new RepeatStrategy.Yearly(), Forever());

        Assert.Equal(new[] { D(2024, 2, 29) }, Dates(account.GetMonth(M(2024, 2))));
        Assert.Equal(new[] { D(2025, 2, 28) }, Dates(account.GetMonth(M(2025, 2))));
        Assert.Equal(new[] { D(2026, 2, 28) }, Dates(account.GetMonth(M(2026, 2))));
        Assert.Equal(new[] { D(2027, 2, 28) }, Dates(account.GetMonth(M(2027, 2))));
        Assert.Equal(new[] { D(2028, 2, 29) }, Dates(account.GetMonth(M(2028, 2)))); // restored in the leap year
    }
}
