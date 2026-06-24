using MMoney.Core;
using Xunit;
using static MMoney.Core.Tests.TestHelpers;

namespace MMoney.Core.Tests;

public class SequenceDateTests
{
    private static DateOnly[] OccurrenceDates(IEnumerable<LedgerEntry> rows) =>
        [.. rows.Where(r => r.Kind == LedgerEntryKind.Occurrence).Select(r => r.Date)];

    [Fact]
    public void ChangeSequenceDate_FromOrigin_ReAnchorsTheWholeSequence()
    {
        var account = NewAccount();
        var origin = account.AddTransaction(D(2026, 1, 15), 10m, "rent", MonthlyOnDay(), Forever());

        account.ChangeSequenceDate(origin, D(2026, 1, 15), D(2026, 1, 20));

        Assert.Equal(new[] { D(2026, 1, 20) }, OccurrenceDates(account.GetMonth(M(2026, 1))));
        Assert.Equal(new[] { D(2026, 2, 20) }, OccurrenceDates(account.GetMonth(M(2026, 2))));
        Assert.Single(account.GetSequences());
    }

    [Fact]
    public void ChangeSequenceDate_FromLaterDate_TruncatesAndRestarts()
    {
        var account = NewAccount();
        account.AddTransaction(D(2026, 1, 15), 10m, "rent", MonthlyOnDay(), Forever());

        var march = account.GetMonth(M(2026, 3))[0].Transaction; // 2026-03-15 occurrence
        account.ChangeSequenceDate(march, D(2026, 3, 15), D(2026, 3, 20));

        Assert.Equal(new[] { D(2026, 2, 15) }, OccurrenceDates(account.GetMonth(M(2026, 2)))); // old, unchanged
        Assert.Equal(new[] { D(2026, 3, 20) }, OccurrenceDates(account.GetMonth(M(2026, 3)))); // new, truncated old
        Assert.Equal(new[] { D(2026, 4, 20) }, OccurrenceDates(account.GetMonth(M(2026, 4))));
        Assert.Equal(2, account.GetSequences().Count); // truncated original + new
    }

    [Fact]
    public void ChangeSequenceDate_NewSequenceKeepsAmountDescriptionAndStrategy()
    {
        var account = NewAccount();
        var origin = account.AddTransaction(D(2026, 1, 15), 10m, "rent", MonthlyOnDay(), Forever());

        account.ChangeSequenceDate(origin, D(2026, 1, 15), D(2026, 1, 20));

        var feb = account.GetMonth(M(2026, 2))[0];
        Assert.Equal(10m, feb.Amount);
        Assert.Equal("rent", feb.Description);
        Assert.Equal(LedgerEntryKind.Occurrence, feb.Kind); // still a repeating sequence
    }

    [Fact]
    public void ChangeSequenceDate_BeforeEditLock_ThrowsAndChangesNothing()
    {
        var account = NewAccount();
        account.AddTransaction(D(2026, 1, 15), 10m, "rent", MonthlyOnDay(), Forever());
        account.AddTransaction(D(2026, 1, 10), 100m, "seed");
        account.CloseMonth(M(2026, 1), today: D(2026, 3, 15)); // edit lock = 2026-02-01

        var feb = OccurrenceTransaction(account, M(2026, 2));

        Assert.Throws<ArgumentOutOfRangeException>(
            () => account.ChangeSequenceDate(feb, D(2026, 1, 20), D(2026, 3, 1))); // fromDate locked
        Assert.Throws<ArgumentOutOfRangeException>(
            () => account.ChangeSequenceDate(feb, D(2026, 2, 15), D(2026, 1, 20))); // newDate locked

        // Nothing changed: the sequence still runs on the 15th, single sequence.
        Assert.Equal(new[] { D(2026, 2, 15) }, OccurrenceDates(account.GetMonth(M(2026, 2))));
        Assert.Single(account.GetSequences());
    }

    [Fact]
    public void ChangeSequenceDate_LockedOrigin_RetiresRemnantAndKeepsOnlyTheNewSequence()
    {
        var account = NewAccount();
        account.AddTransaction(D(2026, 1, 15), 10m, "rent", MonthlyOnDay(), Forever());
        account.AddTransaction(D(2026, 1, 10), 100m, "seed");
        account.CloseMonth(M(2026, 1), today: D(2026, 3, 15)); // edit lock = 2026-02-01; origin (01-15) now locked

        var feb = OccurrenceTransaction(account, M(2026, 2));
        // "All" scope on a locked origin: caller passes fromDate = max(origin, lock) = the lock.
        account.ChangeSequenceDate(feb, D(2026, 2, 1), D(2026, 2, 20));

        Assert.Single(account.GetSequences()); // truncated remnant (ends before the lock) retired
        Assert.Equal(new[] { D(2026, 2, 20) }, OccurrenceDates(account.GetMonth(M(2026, 2))));
        Assert.Equal(new[] { D(2026, 3, 20) }, OccurrenceDates(account.GetMonth(M(2026, 3))));
    }

    private static Transaction OccurrenceTransaction(Account account, MonthOnly month) =>
        account.GetMonth(month).First(r => r.Kind == LedgerEntryKind.Occurrence).Transaction;
}
