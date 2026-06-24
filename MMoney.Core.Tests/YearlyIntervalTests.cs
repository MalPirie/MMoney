using System.Text.Json;
using MMoney.Core;
using MMoney.Core.Repeat;
using Xunit;
using static MMoney.Core.Tests.TestHelpers;

namespace MMoney.Core.Tests;

public class YearlyIntervalTests
{
    private static DateOnly[] Dates(IEnumerable<LedgerEntry> rows) => [.. rows.Select(r => r.Date)];

    [Fact]
    public void Yearly_EveryNYears_SkipsInterveningYears()
    {
        var account = NewAccount();
        account.AddTransaction(D(2025, 3, 10), 100m, "insurance", new RepeatStrategy.Yearly(2), Forever());

        Assert.Single(account.GetMonth(M(2025, 3)));
        Assert.Empty(account.GetMonth(M(2026, 3)));
        Assert.Single(account.GetMonth(M(2027, 3)));
        Assert.Empty(account.GetMonth(M(2028, 3)));
        Assert.Single(account.GetMonth(M(2029, 3)));
        Assert.Equal(300m, account.BalanceOn(D(2030, 1, 1))); // 2025, 2027, 2029
    }

    [Fact]
    public void Yearly_LeapDayOrigin_EveryTwoYears_RestoresFeb29InLeapYears()
    {
        var account = NewAccount();
        account.AddTransaction(D(2024, 2, 29), 50m, "leap", new RepeatStrategy.Yearly(2), Forever());

        Assert.Equal(new[] { D(2024, 2, 29) }, Dates(account.GetMonth(M(2024, 2))));
        Assert.Empty(account.GetMonth(M(2025, 2)));                                  // odd year, no occurrence
        Assert.Equal(new[] { D(2026, 2, 28) }, Dates(account.GetMonth(M(2026, 2)))); // clamped
        Assert.Equal(new[] { D(2028, 2, 29) }, Dates(account.GetMonth(M(2028, 2)))); // restored in the leap year
        Assert.Equal(new[] { D(2030, 2, 28) }, Dates(account.GetMonth(M(2030, 2))));
    }

    [Fact]
    public void Yearly_LegacyEventWithoutInterval_ReplaysAsEveryYear()
    {
        // A pre-interval log line: "yearly" with no Interval field. It must still decode and replay annually.
        const string json =
            """
            {"$type":"transactionAdded","Date":"2024-03-10","Sequence":1,"Amount":100,"Description":"sub","Strategy":{"$repeat":"yearly"},"EndCondition":{"$until":"forever"}}
            """;
        var legacyEvent = JsonSerializer.Deserialize<AccountEvent>(json)!;

        var account = new Account(Guid.NewGuid(), [legacyEvent]);

        Assert.Single(account.GetMonth(M(2024, 3)));
        Assert.Single(account.GetMonth(M(2025, 3)));
        Assert.Single(account.GetMonth(M(2026, 3)));
        Assert.Equal(300m, account.BalanceOn(D(2026, 12, 31))); // every year: 2024, 2025, 2026
    }
}
