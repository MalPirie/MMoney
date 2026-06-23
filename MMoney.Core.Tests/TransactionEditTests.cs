using MMoney.Core;
using Xunit;
using static MMoney.Core.Tests.TestHelpers;

namespace MMoney.Core.Tests;

public class TransactionEditTests
{
    [Fact]
    public void MovingAOneOff_AcrossMonths_LeavesTheOldMonthEmpty()
    {
        var account = NewAccount();
        var tx = account.AddTransaction(D(2026, 1, 10), 100m, "x");

        account.ChangeTransactionDate(tx, D(2026, 2, 20));

        Assert.Empty(account.GetMonth(M(2026, 1)));
        var feb = account.GetMonth(M(2026, 2));
        Assert.Single(feb);
        Assert.Equal(D(2026, 2, 20), feb[0].Date);
        Assert.Equal(LedgerEntryKind.OneOff, feb[0].Kind);
    }

    [Fact]
    public void RemovingAOneOff_ClearsItEntirely()
    {
        var account = NewAccount();
        var tx = account.AddTransaction(D(2026, 1, 10), 100m, "x");

        account.RemoveTransaction(tx);

        Assert.Empty(account.GetMonth(M(2026, 1)));
        Assert.Equal(0m, account.BalanceOn(D(2026, 1, 31)));
    }

    [Fact]
    public void RemovingAMovedInOccurrence_DeletesItAndLeavesTheOriginSkipped()
    {
        var account = NewAccount();
        account.AddTransaction(D(2026, 1, 15), 10m, "rent", MonthlyOnDay(), Forever());

        var feb = account.GetMonth(M(2026, 2))[0].Transaction; // 2026-02-15
        account.ChangeTransactionDate(feb, D(2026, 2, 20));     // move to a non-natural date
        var moved = account.GetMonth(M(2026, 2))[0].Transaction; // 2026-02-20

        account.RemoveTransaction(moved);

        Assert.Empty(account.GetMonth(M(2026, 2))); // original 02-15 stays tombstoned, 02-20 gone
        // The sequence is otherwise intact.
        Assert.Single(account.GetMonth(M(2026, 3)));
    }

    [Fact]
    public void MovingAnOccurrence_OntoAnotherOccurrenceOfTheSameSequence_Throws()
    {
        var account = NewAccount();
        account.AddTransaction(D(2026, 1, 15), 10m, "rent", MonthlyOnDay(), Forever());

        var feb = account.GetMonth(M(2026, 2))[0].Transaction; // 2026-02-15

        Assert.Throws<InvalidOperationException>(() => account.ChangeTransactionDate(feb, D(2026, 3, 15)));
    }
}
