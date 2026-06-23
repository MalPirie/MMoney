using MMoney.Core;
using Xunit;
using static MMoney.Core.Tests.TestHelpers;

namespace MMoney.Core.Tests;

public class ValidationTests
{
    [Fact]
    public void AddTransaction_RejectsZeroAmount()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => NewAccount().AddTransaction(D(2026, 1, 1), 0m, "x"));
    }

    [Fact]
    public void AddTransaction_RejectsBlankDescription()
    {
        Assert.Throws<ArgumentException>(() => NewAccount().AddTransaction(D(2026, 1, 1), 5m, "   "));
    }

    [Fact]
    public void AddTransaction_RejectsDateBeforeEarliestAllowed()
    {
        var account = NewAccount();
        account.AddTransaction(D(2026, 1, 10), 100m, "a");
        account.AddTransaction(D(2026, 2, 10), 50m, "b");
        account.CloseMonth(M(2026, 1), today: D(2026, 2, 15)); // earliest becomes 2026-02-01

        Assert.Throws<ArgumentOutOfRangeException>(() => account.AddTransaction(D(2026, 1, 20), 5m, "late"));
    }

    [Fact]
    public void ChangeTransactionAmount_OnMissingTransaction_Throws()
    {
        var account = NewAccount();
        var ghost = new Transaction(new TransactionId(D(2026, 1, 1), 99), 5m, "ghost");

        Assert.Throws<InvalidOperationException>(() => account.ChangeTransactionAmount(ghost, 10m));
    }
}
