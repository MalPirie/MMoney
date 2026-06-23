using MMoney.Core;
using MMoney.Core.Repeat;
using Xunit;
using static MMoney.Core.Tests.TestHelpers;

namespace MMoney.Core.Tests;

public class ScheduleCornerTests
{
    private static DateOnly[] Dates(IEnumerable<LedgerEntry> rows) => [.. rows.Select(r => r.Date)];

    [Fact]
    public void Weekly_EveryOtherWeek_KeepsCadenceAcrossAYearBoundary()
    {
        // 2025-12-01 is a Monday; bi-weekly Mondays must keep 14-day spacing across into 2026.
        var account = NewAccount();
        account.AddTransaction(D(2025, 12, 1), 5m, "x", new RepeatStrategy.Weekly(2, DaysOfWeek.Monday), Forever());

        Assert.Equal(new[] { D(2025, 12, 1), D(2025, 12, 15), D(2025, 12, 29) }, Dates(account.GetMonth(M(2025, 12))));
        Assert.Equal(new[] { D(2026, 1, 12), D(2026, 1, 26) }, Dates(account.GetMonth(M(2026, 1))));
    }

    [Fact]
    public void AfterOccurrences_CountsWeekdayBasedMonthlyOccurrences()
    {
        // First Monday of the month, stop after 3: 2026-06-01, 2026-07-06, 2026-08-03.
        var account = NewAccount();
        account.AddTransaction(D(2026, 6, 1), 10m, "x",
            new RepeatStrategy.Monthly(1, DayInMonth.First), new RepeatEndCondition.AfterOccurrences(3));

        Assert.Equal(new[] { D(2026, 6, 1) }, Dates(account.GetMonth(M(2026, 6))));
        Assert.Equal(new[] { D(2026, 7, 6) }, Dates(account.GetMonth(M(2026, 7))));
        Assert.Equal(new[] { D(2026, 8, 3) }, Dates(account.GetMonth(M(2026, 8))));
        Assert.Empty(account.GetMonth(M(2026, 9)));
        Assert.Equal(30m, account.BalanceOn(D(2026, 12, 31))); // exactly 3 occurrences
    }

    [Fact]
    public void TwoSequencesOnTheSameDate_OrderBySequenceNumberInTheRunningBalance()
    {
        var account = NewAccount();
        account.AddTransaction(D(2026, 1, 15), 10m, "first", MonthlyOnDay(), Forever());  // sequence 1
        account.AddTransaction(D(2026, 1, 15), 20m, "second", MonthlyOnDay(), Forever()); // sequence 2

        var june = account.GetMonth(M(2026, 6));

        Assert.Equal(2, june.Count);
        Assert.All(june, r => Assert.Equal(D(2026, 6, 15), r.Date));
        Assert.Equal(10m, june[0].Amount); // lower sequence number first
        Assert.Equal(20m, june[1].Amount);
        // Jan..May = 5 months x 30 = 150 opening; then +10, +20 on 15 June.
        Assert.Equal(160m, june[0].Balance);
        Assert.Equal(180m, june[1].Balance);
    }
}
