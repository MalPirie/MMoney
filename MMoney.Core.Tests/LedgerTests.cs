using MMoney.Core;
using Xunit;
using static MMoney.Core.Tests.TestHelpers;

namespace MMoney.Core.Tests;

public class LedgerTests
{
    [Fact]
    public void OneOffs_ProduceRunningBalances()
    {
        var account = NewAccount();
        account.AddTransaction(D(2026, 6, 1), 100m, "open");
        account.AddTransaction(D(2026, 6, 5), -30m, "spend");

        var rows = account.GetMonth(M(2026, 6));

        Assert.Equal(2, rows.Count);
        Assert.Equal(100m, rows[0].Balance);
        Assert.Equal(70m, rows[1].Balance);
        Assert.All(rows, r => Assert.Equal(LedgerEntryKind.OneOff, r.Kind));
    }

    [Fact]
    public void BalanceOn_IsInclusiveAndZeroBeforeAnyTransaction()
    {
        var account = NewAccount();
        account.AddTransaction(D(2026, 6, 1), 100m, "open");
        account.AddTransaction(D(2026, 6, 5), -30m, "spend");

        Assert.Equal(0m, account.BalanceOn(D(2026, 5, 31)));
        Assert.Equal(100m, account.BalanceOn(D(2026, 6, 1)));
        Assert.Equal(100m, account.BalanceOn(D(2026, 6, 4)));
        Assert.Equal(70m, account.BalanceOn(D(2026, 6, 5)));
        Assert.Equal(70m, account.BalanceOn(D(2030, 1, 1)));
    }

    [Fact]
    public void EmptyAccount_HasZeroBalanceAndNoRows()
    {
        var account = NewAccount();

        Assert.Equal(0m, account.BalanceOn(D(2026, 6, 1)));
        Assert.Empty(account.GetMonth(M(2026, 6)));
    }
}
