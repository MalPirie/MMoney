using MMoney.App.Ledger;
using MMoney.Core;
using Xunit;

namespace MMoney.App.Tests;

/// <summary>
/// The month-ledger presentation transform (app-design §5): ascending Core output → day-grouped descending view.
/// </summary>
public class MonthLedgerTests
{
    private static LedgerEntry Entry(int day, int seq, decimal amount, decimal balance,
        LedgerEntryKind kind = LedgerEntryKind.OneOff) =>
        new(new Transaction(new DateOnly(2026, 3, day), seq, amount, $"txn {day}.{seq}"), balance, kind);

    [Fact]
    public void ByDayDescending_OrdersDaysLatestFirst()
    {
        var view = MonthLedger.ByDayDescending(new[]
        {
            Entry(5, 1, 10m, 10m),
            Entry(10, 1, 20m, 30m),
            Entry(20, 1, -5m, 25m),
        });

        Assert.Equal(
            new[] { new DateOnly(2026, 3, 20), new DateOnly(2026, 3, 10), new DateOnly(2026, 3, 5) },
            view.Select(day => day.Date).ToArray());
    }

    [Fact]
    public void ByDayDescending_WithinADay_OrdersBySequenceDescending()
    {
        var view = MonthLedger.ByDayDescending(new[]
        {
            Entry(5, 1, 10m, 10m),
            Entry(5, 2, 20m, 30m),
            Entry(5, 3, -5m, 25m),
        });

        var day = Assert.Single(view);
        Assert.Equal(new[] { 3, 2, 1 }, day.Entries.Select(e => e.Transaction.Sequence).ToArray());
    }

    [Fact]
    public void ByDayDescending_LandsTheCarriedBalanceAnchorLast()
    {
        // The carried balance is sequence 0 on the earliest day — it must fall at the very bottom of the view.
        var view = MonthLedger.ByDayDescending(new[]
        {
            Entry(1, 0, 100m, 100m, LedgerEntryKind.CarriedBalance),
            Entry(1, 1, 10m, 110m),
            Entry(9, 1, 20m, 130m),
        });

        var flattened = view.SelectMany(day => day.Entries).ToList();
        Assert.Equal(LedgerEntryKind.CarriedBalance, flattened[^1].Kind);
    }

    [Fact]
    public void ByDayDescending_PreservesTheCoreComputedBalances() =>
        Assert.Equal(30m, MonthLedger.ByDayDescending(new[] { Entry(10, 1, 20m, 30m) })[0].Entries[0].Balance);

    [Fact]
    public void ByDayDescending_EmptyInput_YieldsNoDays() =>
        Assert.Empty(MonthLedger.ByDayDescending(System.Array.Empty<LedgerEntry>()));

    private static readonly DateOnly Today = new(2026, 3, 15);

    [Fact]
    public void WithToday_TodayAlreadyPresent_ReturnsUnchanged()
    {
        var days = MonthLedger.ByDayDescending(new[] { Entry(15, 1, 10m, 10m) });

        var result = MonthLedger.WithToday(days, Today);

        Assert.Same(days, result);
    }

    [Fact]
    public void WithToday_InsertsBetweenFutureAndPastDays()
    {
        var days = MonthLedger.ByDayDescending(new[]
        {
            Entry(20, 1, 10m, 10m), // after today
            Entry(10, 1, 10m, 20m), // before today
        });

        var result = MonthLedger.WithToday(days, Today);

        Assert.Equal(
            new[] { new DateOnly(2026, 3, 20), Today, new DateOnly(2026, 3, 10) },
            result.Select(d => d.Date).ToArray());
        Assert.Empty(result[1].Entries);
    }

    [Fact]
    public void WithToday_AllDaysInTheFuture_LandsAtTheBottom()
    {
        var days = MonthLedger.ByDayDescending(new[] { Entry(20, 1, 10m, 10m) });

        var result = MonthLedger.WithToday(days, Today);

        Assert.Equal(new[] { new DateOnly(2026, 3, 20), Today }, result.Select(d => d.Date).ToArray());
    }

    [Fact]
    public void WithToday_AllDaysInThePast_LandsAtTheTop()
    {
        var days = MonthLedger.ByDayDescending(new[] { Entry(10, 1, 10m, 10m) });

        var result = MonthLedger.WithToday(days, Today);

        Assert.Equal(new[] { Today, new DateOnly(2026, 3, 10) }, result.Select(d => d.Date).ToArray());
    }

    [Fact]
    public void WithToday_NoDays_YieldsOnlyToday()
    {
        var result = MonthLedger.WithToday(System.Array.Empty<LedgerDay>(), Today);

        var day = Assert.Single(result);
        Assert.Equal(Today, day.Date);
        Assert.Empty(day.Entries);
    }
}
