using MMoney.Core;
using MMoney.Core.Repeat;
using Xunit;
using static MMoney.Core.Tests.TestHelpers;

namespace MMoney.Core.Tests;

public class MonthCloseTests
{
    [Fact]
    public void ClosingAMonth_CollapsesItIntoACarriedBalance()
    {
        var account = NewAccount();
        account.AddTransaction(D(2026, 1, 10), 100m, "open");
        account.AddTransaction(D(2026, 2, 10), -40m, "spend");

        account.CloseMonth(M(2026, 1), today: D(2026, 2, 15));

        Assert.Equal(D(2026, 2, 1), account.EarliestAllowedDate);
        Assert.Empty(account.GetMonth(M(2026, 1)));        // collapsed
        Assert.Equal(0m, account.BalanceOn(D(2026, 1, 31))); // before the anchor

        var feb = account.GetMonth(M(2026, 2));
        Assert.Equal(2, feb.Count);
        Assert.Equal(LedgerEntryKind.CarriedBalance, feb[0].Kind);
        Assert.Equal(100m, feb[0].Balance);
        Assert.Equal(60m, feb[1].Balance);
        Assert.Equal(60m, account.BalanceOn(D(2026, 2, 28)));
    }

    [Fact]
    public void CloseMonth_RejectsCurrentFutureAndNonOldestMonths()
    {
        var account = NewAccount();
        account.AddTransaction(D(2026, 1, 10), 100m, "a");
        account.AddTransaction(D(2026, 2, 10), 50m, "b");

        // Current month.
        Assert.Throws<InvalidOperationException>(() => account.CloseMonth(M(2026, 3), D(2026, 3, 15)));
        // Future month.
        Assert.Throws<InvalidOperationException>(() => account.CloseMonth(M(2026, 2), D(2026, 1, 15)));
        // Not the oldest month with content (Jan is still open).
        Assert.Throws<InvalidOperationException>(() => account.CloseMonth(M(2026, 2), D(2026, 3, 15)));
    }

    [Fact]
    public void ClosableMonth_TracksExactlyWhatCloseMonthWouldAccept()
    {
        var account = NewAccount();
        Assert.Null(account.ClosableMonth(D(2026, 3, 15))); // no activity yet

        account.AddTransaction(D(2026, 1, 10), 100m, "a");
        account.AddTransaction(D(2026, 2, 10), 50m, "b");

        Assert.Equal(M(2026, 1), account.ClosableMonth(D(2026, 3, 15))); // oldest content month, in the past
        Assert.Null(account.ClosableMonth(D(2026, 1, 15)));              // oldest activity is the current month

        account.CloseMonth(M(2026, 1), D(2026, 3, 15));
        Assert.Equal(M(2026, 2), account.ClosableMonth(D(2026, 3, 15))); // Feb is now the oldest open past month

        account.CloseMonth(M(2026, 2), D(2026, 3, 15));
        Assert.Null(account.ClosableMonth(D(2026, 3, 15)));              // only the current month remains
    }

    [Fact]
    public void ClosableMonth_IsNullWhileShowingClosedMonths()
    {
        var (account, events) = Recording();
        account.AddTransaction(D(2026, 1, 10), 100m, "a");
        var shown = new Account(account.Id, events, ignoreMonthClosed: true);

        Assert.Null(shown.ClosableMonth(D(2026, 3, 15)));
    }

    [Fact]
    public void IgnoreMonthClosed_ShowsClosedMonthsButKeepsThemReadOnly()
    {
        var (account, events) = Recording();
        account.AddTransaction(D(2026, 1, 10), 100m, "a");
        account.AddTransaction(D(2026, 2, 10), -40m, "b");
        account.CloseMonth(M(2026, 1), today: D(2026, 2, 15));

        var shown = new Account(account.Id, events, ignoreMonthClosed: true);

        Assert.Equal(D(2026, 2, 1), shown.EarliestAllowedDate); // lock still applied
        var jan = shown.GetMonth(M(2026, 1));
        Assert.Single(jan);
        Assert.Equal(100m, jan[0].Balance);

        var janTx = jan[0].Transaction;
        Assert.Throws<ArgumentOutOfRangeException>(() => shown.ChangeTransactionAmount(janTx, 5m));
    }

    [Fact]
    public void CannotCloseAMonthWhileShowingClosedMonths()
    {
        var (account, events) = Recording();
        account.AddTransaction(D(2026, 1, 10), 100m, "a");
        var shown = new Account(account.Id, events, ignoreMonthClosed: true);

        Assert.Throws<InvalidOperationException>(() => shown.CloseMonth(M(2026, 1), D(2026, 3, 15)));
    }

    [Fact]
    public void FarFutureOneOff_DoesNotThrowAndProjectsAlongsideOccurrences()
    {
        var account = NewAccount();
        account.AddTransaction(D(2026, 1, 15), 10m, "rent", MonthlyOnDay(), Forever());

        account.AddTransaction(D(2030, 6, 10), 500m, "bonus"); // used to throw under eager materialisation

        var rows = account.GetMonth(M(2030, 6));
        Assert.Equal(2, rows.Count);
        Assert.Equal(D(2030, 6, 10), rows[0].Date);
        Assert.Equal(LedgerEntryKind.OneOff, rows[0].Kind);
        Assert.Equal(D(2030, 6, 15), rows[1].Date);
        Assert.Equal(LedgerEntryKind.Occurrence, rows[1].Kind);
    }

    [Fact]
    public void CompletedSequence_IsPrunedOnceItEndsBeforeTheEarliestDate()
    {
        var account = NewAccount();
        account.AddTransaction(D(2026, 1, 15), 10m, "ending", MonthlyOnDay(), new RepeatEndCondition.UntilDate(D(2026, 3, 31)));
        account.AddTransaction(D(2026, 1, 20), 20m, "ongoing", MonthlyOnDay(), Forever());
        account.AddTransaction(D(2026, 4, 10), 5m, "anchor-helper");

        Assert.Equal(2, account.GetSequences().Count);

        account.CloseMonth(M(2026, 1), D(2026, 5, 15));
        account.CloseMonth(M(2026, 2), D(2026, 5, 15));
        account.CloseMonth(M(2026, 3), D(2026, 5, 15));

        Assert.Equal(D(2026, 4, 1), account.EarliestAllowedDate);
        Assert.Single(account.GetSequences()); // the ended sequence is gone
    }

    [Fact]
    public void IgnoreMonthClosed_KeepsSequencesThatCompletedInsideAClosedMonth()
    {
        var (account, events) = Recording();
        account.AddTransaction(D(2026, 1, 15), -10m, "ending", MonthlyOnDay(), new RepeatEndCondition.UntilDate(D(2026, 3, 15)));
        account.AddTransaction(D(2026, 4, 10), 500m, "anchor-helper");

        // One occurrence carries an override; the other two stay at the sequence's base amount.
        var february = account.GetMonth(M(2026, 2))[0].Transaction;
        account.ChangeTransactionAmount(february, -25m);

        account.CloseMonth(M(2026, 1), D(2026, 5, 15));
        account.CloseMonth(M(2026, 2), D(2026, 5, 15));
        account.CloseMonth(M(2026, 3), D(2026, 5, 15));

        var shown = new Account(account.Id, events, ignoreMonthClosed: true);

        // Every occurrence reappears - the overridden one at its own amount, the rest at the base - and all
        // of them still resolve through the rule rather than degrading into one-offs.
        foreach (var (month, amount) in new[] { (M(2026, 1), -10m), (M(2026, 2), -25m), (M(2026, 3), -10m) })
        {
            var rows = shown.GetMonth(month);
            Assert.Single(rows);
            Assert.Equal(amount, rows[0].Amount);
            Assert.Equal(LedgerEntryKind.Occurrence, rows[0].Kind);
        }

        Assert.Equal(D(2026, 4, 1), shown.EarliestAllowedDate); // lock still advances while browsing
    }

    [Fact]
    public void IgnoreMonthClosed_ShowsExactlyWhatTheLogHoldsBeforeAnyClose()
    {
        var (account, events) = Recording();
        account.AddTransaction(D(2026, 1, 15), -10m, "ending", MonthlyOnDay(), new RepeatEndCondition.UntilDate(D(2026, 3, 15)));
        account.AddTransaction(D(2026, 1, 20), -20m, "ongoing", MonthlyOnDay(), Forever());
        account.AddTransaction(D(2026, 2, 5), -7m, "one-off");
        account.AddTransaction(D(2026, 4, 10), 500m, "anchor-helper");

        var beforeClosing = Rows(new Account(account.Id, events));

        account.CloseMonth(M(2026, 1), D(2026, 5, 15));
        account.CloseMonth(M(2026, 2), D(2026, 5, 15));
        account.CloseMonth(M(2026, 3), D(2026, 5, 15));

        // Browsing closed months must reproduce the pre-close ledger exactly: the collapse is presentational.
        Assert.Equal(beforeClosing, Rows(new Account(account.Id, events, ignoreMonthClosed: true)));
    }

    [Fact]
    public void CompletedSequence_IsStillPrunedWhenTheCloseIsApplied()
    {
        var (account, events) = Recording();
        account.AddTransaction(D(2026, 1, 15), 10m, "ending", MonthlyOnDay(), new RepeatEndCondition.UntilDate(D(2026, 3, 31)));
        account.AddTransaction(D(2026, 4, 10), 5m, "anchor-helper");

        account.CloseMonth(M(2026, 1), D(2026, 5, 15));
        account.CloseMonth(M(2026, 2), D(2026, 5, 15));
        account.CloseMonth(M(2026, 3), D(2026, 5, 15));

        Assert.Empty(account.GetSequences());
        Assert.Empty(new Account(account.Id, events).GetSequences()); // and on replay
    }

    /// <summary>Every ledger row an account holds, flattened for whole-ledger equality assertions.</summary>
    private static List<(DateOnly Date, decimal Amount, string Description, LedgerEntryKind Kind)> Rows(Account account) =>
        account.EarliestContentMonth() is { } first
            ? [.. first.To(M(2026, 12)).SelectMany(account.GetMonth)
                .Select(e => (e.Date, e.Amount, e.Description, e.Kind))]
            : [];

    [Fact]
    public void Replay_ReconstructsTheSameBalances()
    {
        var (account, events) = Recording();
        account.AddTransaction(D(2026, 1, 15), 10m, "rent", MonthlyOnDay(), Forever());
        account.AddTransaction(D(2026, 2, 3), 200m, "salary");
        var march = account.GetMonth(M(2026, 3))[0].Transaction;
        account.ChangeTransactionAmount(march, 99m);
        account.CloseMonth(M(2026, 1), today: D(2026, 4, 15));

        var replayed = new Account(account.Id, events);

        Assert.Equal(account.BalanceOn(D(2026, 4, 30)), replayed.BalanceOn(D(2026, 4, 30)));
        Assert.Equal(account.EarliestAllowedDate, replayed.EarliestAllowedDate);
        Assert.Equal(account.GetMonth(M(2026, 3)).Count, replayed.GetMonth(M(2026, 3)).Count);
        Assert.Equal(99m, replayed.GetMonth(M(2026, 3))[0].Amount);
    }
}
