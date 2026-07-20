using MMoney.App.Export;
using MMoney.Core;
using Xunit;

namespace MMoney.App.Tests;

public class LedgerExportTests
{
    // ---- ToCsv -----------------------------------------------------------------------------------

    [Fact]
    public void ToCsv_EmptyRows_WritesHeaderOnly()
    {
        var csv = LedgerExport.ToCsv([]);

        Assert.Equal("Date,Description,Amount,Balance\r\n", csv);
    }

    [Fact]
    public void ToCsv_FormatsIsoDateSignedAmountAndBalance()
    {
        var rows = new[]
        {
            Row(new DateOnly(2026, 5, 10), 100m, 100m, "Opening"),
            Row(new DateOnly(2026, 6, 15), -30.5m, 69.5m, "Groceries"),
        };

        var csv = LedgerExport.ToCsv(rows);

        Assert.Equal(
            "Date,Description,Amount,Balance\r\n"
            + "2026-05-10,Opening,100.00,100.00\r\n"
            + "2026-06-15,Groceries,-30.50,69.50\r\n",
            csv);
    }

    [Theory]
    [InlineData("Coffee, tea", "\"Coffee, tea\"")]        // comma → quoted
    [InlineData("Say \"hi\"", "\"Say \"\"hi\"\"\"")]      // inner quotes doubled + wrapped
    [InlineData("Line1\nLine2", "\"Line1\nLine2\"")]      // newline → quoted
    [InlineData("Plain text", "Plain text")]               // nothing special → bare
    public void ToCsv_EscapesDescriptionPerRfc4180(string description, string expectedField)
    {
        var csv = LedgerExport.ToCsv([Row(new DateOnly(2026, 5, 10), 1m, 1m, description)]);

        Assert.Equal($"Date,Description,Amount,Balance\r\n2026-05-10,{expectedField},1.00,1.00\r\n", csv);
    }

    // ---- CollectRows -----------------------------------------------------------------------------

    [Fact]
    public void CollectRows_EmptyAccount_ReturnsEmpty()
    {
        var account = NewAccount();

        Assert.Empty(LedgerExport.CollectRows(account, new DateOnly(2026, 7, 20)));
    }

    [Fact]
    public void CollectRows_SpansFirstContentMonthThroughToday_WithRunningBalance()
    {
        var account = NewAccount();
        account.AddTransaction(new DateOnly(2026, 5, 10), 100m, "May in");
        account.AddTransaction(new DateOnly(2026, 6, 15), -30m, "June out");
        account.AddTransaction(new DateOnly(2026, 7, 5), -20m, "July out");

        var rows = LedgerExport.CollectRows(account, new DateOnly(2026, 7, 20));

        Assert.Equal(3, rows.Count);
        Assert.Equal(new DateOnly(2026, 5, 10), rows[0].Date);
        Assert.Equal(100m, rows[0].Balance); // opening month
        Assert.Equal(70m, rows[1].Balance);  // 100 - 30 carried into June
        Assert.Equal(50m, rows[2].Balance);  // 70 - 20 carried into July
    }

    [Fact]
    public void CollectRows_ExcludesMonthsAfterToday()
    {
        var account = NewAccount();
        account.AddTransaction(new DateOnly(2026, 7, 5), -20m, "This month");
        account.AddTransaction(new DateOnly(2026, 8, 1), -99m, "Next month");

        var rows = LedgerExport.CollectRows(account, new DateOnly(2026, 7, 20));

        Assert.Single(rows);
        Assert.Equal(new DateOnly(2026, 7, 5), rows[0].Date);
    }

    [Fact]
    public void CollectRows_TodayBeforeFirstContent_ReturnsEmpty()
    {
        var account = NewAccount();
        account.AddTransaction(new DateOnly(2026, 7, 5), -20m, "July");

        Assert.Empty(LedgerExport.CollectRows(account, new DateOnly(2026, 6, 30)));
    }

    private static LedgerEntry Row(DateOnly date, decimal amount, decimal balance, string description) =>
        new(new Transaction(date, 1, amount, description), balance, LedgerEntryKind.OneOff);

    private static Account NewAccount() => new(Guid.NewGuid(), []);
}
