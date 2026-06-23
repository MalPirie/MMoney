using MMoney.Core;
using MMoney.Core.Repeat;
using Xunit;
using static MMoney.Core.Tests.TestHelpers;

namespace MMoney.Core.Tests;

public class MonthlyScheduleTests
{
    private static DateOnly[] Dates(IEnumerable<LedgerEntry> rows) => [.. rows.Select(r => r.Date)];

    [Fact]
    public void Monthly_DayOfMonth_ClampsToShortMonths()
    {
        var account = NewAccount();
        account.AddTransaction(D(2026, 1, 31), 10m, "rent", new RepeatStrategy.Monthly(1, DayInMonth.DayOfMonth), Forever());

        Assert.Equal(new[] { D(2026, 1, 31) }, Dates(account.GetMonth(M(2026, 1))));
        Assert.Equal(new[] { D(2026, 2, 28) }, Dates(account.GetMonth(M(2026, 2)))); // 2026 is not a leap year
        Assert.Equal(new[] { D(2026, 3, 31) }, Dates(account.GetMonth(M(2026, 3))));
        Assert.Equal(new[] { D(2026, 4, 30) }, Dates(account.GetMonth(M(2026, 4))));
    }

    [Fact]
    public void Monthly_FirstWeekday_LandsOnTheFirstSuchWeekday()
    {
        // 2026-06-01 is the first Monday of June.
        var account = NewAccount();
        account.AddTransaction(D(2026, 6, 1), 10m, "payday", new RepeatStrategy.Monthly(1, DayInMonth.First), Forever());

        Assert.Equal(new[] { D(2026, 6, 1) }, Dates(account.GetMonth(M(2026, 6))));
        Assert.Equal(new[] { D(2026, 7, 6) }, Dates(account.GetMonth(M(2026, 7))));
    }

    [Fact]
    public void Monthly_LastWeekday_LandsOnTheLastSuchWeekday()
    {
        // 2026-06-29 is the last Monday of June.
        var account = NewAccount();
        account.AddTransaction(D(2026, 6, 29), 10m, "payday", new RepeatStrategy.Monthly(1, DayInMonth.Last), Forever());

        Assert.Equal(new[] { D(2026, 6, 29) }, Dates(account.GetMonth(M(2026, 6))));
        Assert.Equal(new[] { D(2026, 7, 27) }, Dates(account.GetMonth(M(2026, 7))));
    }

    [Fact]
    public void Monthly_EveryOtherMonth_SkipsTheInterveningMonths()
    {
        var account = NewAccount();
        account.AddTransaction(D(2026, 1, 15), 10m, "x", new RepeatStrategy.Monthly(2, DayInMonth.DayOfMonth), Forever());

        Assert.Single(account.GetMonth(M(2026, 1)));
        Assert.Empty(account.GetMonth(M(2026, 2)));
        Assert.Single(account.GetMonth(M(2026, 3)));
        Assert.Empty(account.GetMonth(M(2026, 4)));
        Assert.Single(account.GetMonth(M(2026, 5)));
    }

    [Fact]
    public void AfterOccurrences_StopsAfterTheGivenCount()
    {
        var account = NewAccount();
        account.AddTransaction(D(2026, 1, 15), 10m, "instalment",
            new RepeatStrategy.Monthly(1, DayInMonth.DayOfMonth), new RepeatEndCondition.AfterOccurrences(3));

        Assert.Single(account.GetMonth(M(2026, 1)));
        Assert.Single(account.GetMonth(M(2026, 3)));
        Assert.Empty(account.GetMonth(M(2026, 4)));
        Assert.Equal(30m, account.BalanceOn(D(2026, 12, 31))); // exactly 3 occurrences
    }
}
