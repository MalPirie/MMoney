using MMoney.Core;
using Xunit;
using static MMoney.Core.Tests.TestHelpers;

namespace MMoney.Core.Tests;

public class SequenceTests
{
    [Fact]
    public void FarFutureMonth_ProjectsOccurrenceWithCorrectRunningBalance()
    {
        var account = NewAccount();
        account.AddTransaction(D(2026, 1, 15), 10m, "rent", MonthlyOnDay(), Forever());

        var rows = account.GetMonth(M(2030, 1));

        Assert.Single(rows);
        Assert.Equal(D(2030, 1, 15), rows[0].Date);
        Assert.Equal(LedgerEntryKind.Occurrence, rows[0].Kind);
        // 2026-01 .. 2030-01 inclusive = 49 occurrences of 10.
        Assert.Equal(490m, rows[0].Balance);
        Assert.Equal(490m, account.BalanceOn(D(2030, 1, 15)));
    }

    [Fact]
    public void SkippingAnOccurrence_RemovesItAndAdjustsLaterBalances()
    {
        var account = NewAccount();
        account.AddTransaction(D(2026, 1, 15), 10m, "rent", MonthlyOnDay(), Forever());

        var march = account.GetMonth(M(2026, 3))[0].Transaction;
        account.RemoveTransaction(march);

        Assert.Empty(account.GetMonth(M(2026, 3)));
        // Jan + Feb + Apr (March skipped) = 30 by 2026-04-15.
        Assert.Equal(30m, account.BalanceOn(D(2026, 4, 15)));
    }

    [Fact]
    public void MovingAnOccurrence_VacatesOriginAndAppearsAtNewDate()
    {
        var account = NewAccount();
        account.AddTransaction(D(2026, 1, 15), 10m, "rent", MonthlyOnDay(), Forever());

        var feb = account.GetMonth(M(2026, 2))[0].Transaction;
        account.ChangeTransactionDate(feb, D(2026, 2, 20));

        var rows = account.GetMonth(M(2026, 2));
        Assert.Single(rows);
        Assert.Equal(D(2026, 2, 20), rows[0].Date);
        Assert.Equal(LedgerEntryKind.Occurrence, rows[0].Kind);
    }

    [Fact]
    public void ModifyingAnOccurrence_OverridesOnlyThatOccurrence()
    {
        var account = NewAccount();
        account.AddTransaction(D(2026, 1, 15), 10m, "rent", MonthlyOnDay(), Forever());

        var march = account.GetMonth(M(2026, 3))[0].Transaction;
        account.ChangeTransactionAmount(march, 99m);

        Assert.Equal(99m, account.GetMonth(M(2026, 3))[0].Amount);
        Assert.Equal(10m, account.GetMonth(M(2026, 2))[0].Amount);
        Assert.Equal(10m, account.GetMonth(M(2026, 4))[0].Amount);
    }

    [Fact]
    public void SequenceAmountChangeFromOrigin_UpdatesInPlaceButKeepsCustomisedOccurrence()
    {
        var account = NewAccount();
        account.AddTransaction(D(2026, 1, 15), 10m, "rent", MonthlyOnDay(), Forever());

        var march = account.GetMonth(M(2026, 3))[0].Transaction;
        account.ChangeTransactionAmount(march, 99m);

        var origin = account.GetMonth(M(2026, 1))[0].Transaction;
        account.ChangeSequenceAmount(origin, D(2026, 1, 15), 20m);

        Assert.Equal(20m, account.GetMonth(M(2026, 2))[0].Amount);
        Assert.Equal(99m, account.GetMonth(M(2026, 3))[0].Amount); // customised override preserved
        Assert.Equal(20m, account.GetMonth(M(2026, 4))[0].Amount);
        Assert.Single(account.GetSequences());
    }

    [Fact]
    public void SequenceAmountChangeFromLaterDate_SplitsIntoTwoSequences()
    {
        var account = NewAccount();
        account.AddTransaction(D(2026, 1, 15), 10m, "rent", MonthlyOnDay(), Forever());

        var march = account.GetMonth(M(2026, 3))[0].Transaction;
        account.ChangeSequenceAmount(march, D(2026, 3, 15), 20m);

        Assert.Equal(10m, account.GetMonth(M(2026, 2))[0].Amount);
        Assert.Equal(20m, account.GetMonth(M(2026, 3))[0].Amount);
        Assert.Equal(20m, account.GetMonth(M(2026, 4))[0].Amount);
        Assert.Equal(2, account.GetSequences().Count);
    }

    [Fact]
    public void RemoveSequenceFromOrigin_RemovesTheWholeSeries()
    {
        var account = NewAccount();
        var origin = account.AddTransaction(D(2026, 1, 15), 10m, "rent", MonthlyOnDay(), Forever());

        account.RemoveSequence(origin, D(2026, 1, 15));

        Assert.Empty(account.GetSequences());
        Assert.Empty(account.GetMonth(M(2026, 5)));
        Assert.Equal(0m, account.BalanceOn(D(2030, 1, 1)));
    }
}
