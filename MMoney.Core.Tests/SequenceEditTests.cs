using MMoney.Core;
using MMoney.Core.Repeat;
using Xunit;
using static MMoney.Core.Tests.TestHelpers;

namespace MMoney.Core.Tests;

public class SequenceEditTests
{
    [Fact]
    public void ChangeStrategy_OneOffToRepeating_ReplacesItWithASequence()
    {
        var account = NewAccount();
        var oneOff = account.AddTransaction(D(2026, 1, 10), 10m, "x");

        account.ChangeSequenceStrategy(oneOff, D(2026, 1, 10), MonthlyOnDay(), Forever());

        Assert.Single(account.GetSequences());
        Assert.Equal(D(2026, 1, 10), account.GetMonth(M(2026, 1))[0].Date);
        Assert.Equal(D(2026, 2, 10), account.GetMonth(M(2026, 2))[0].Date);
    }

    [Fact]
    public void ChangeStrategy_RepeatingToNeverFromOrigin_LeavesASingleTransaction()
    {
        var account = NewAccount();
        var origin = account.AddTransaction(D(2026, 1, 15), 10m, "r", MonthlyOnDay(), Forever());

        account.ChangeSequenceStrategy(origin, D(2026, 1, 15), new RepeatStrategy.Never(), Forever());

        Assert.Empty(account.GetSequences());
        Assert.Single(account.GetMonth(M(2026, 1)));
        Assert.Empty(account.GetMonth(M(2026, 2)));
    }

    [Fact]
    public void ChangeStrategy_RepeatingToRepeatingFromLaterDate_SplitsTheSchedule()
    {
        var account = NewAccount();
        account.AddTransaction(D(2026, 1, 15), 10m, "r", MonthlyOnDay(), Forever());

        var march = account.GetMonth(M(2026, 3))[0].Transaction;
        account.ChangeSequenceStrategy(march, D(2026, 3, 1), new RepeatStrategy.Daily(1), Forever());

        Assert.Equal(D(2026, 2, 15), account.GetMonth(M(2026, 2))[0].Date); // old monthly still runs
        Assert.Equal(31, account.GetMonth(M(2026, 3)).Count);               // new daily takes over in March
        Assert.Equal(2, account.GetSequences().Count);
    }

    [Fact]
    public void ChangeStrategy_ToTheSameSchedule_IsANoOp()
    {
        var account = NewAccount();
        var origin = account.AddTransaction(D(2026, 1, 15), 10m, "r", MonthlyOnDay(), Forever());

        account.ChangeSequenceStrategy(origin, D(2026, 1, 15), MonthlyOnDay(), Forever());

        Assert.Single(account.GetSequences());
    }

    [Fact]
    public void ChangeStrategy_FromBeforeOrigin_Throws()
    {
        var account = NewAccount();
        var origin = account.AddTransaction(D(2026, 2, 15), 10m, "r", MonthlyOnDay(), Forever());

        Assert.Throws<ArgumentOutOfRangeException>(
            () => account.ChangeSequenceStrategy(origin, D(2026, 2, 1), new RepeatStrategy.Daily(1), Forever()));
    }

    [Fact]
    public void RemoveSequence_FromLaterDate_TruncatesButKeepsTheSequence()
    {
        var account = NewAccount();
        var origin = account.AddTransaction(D(2026, 1, 15), 10m, "r", MonthlyOnDay(), Forever());

        account.RemoveSequence(origin, D(2026, 3, 15));

        Assert.Single(account.GetMonth(M(2026, 2)));
        Assert.Empty(account.GetMonth(M(2026, 3)));
        Assert.Single(account.GetSequences()); // truncated, still present
    }

    [Fact]
    public void ChangeSequenceDescription_FromOrigin_UpdatesInPlace()
    {
        var account = NewAccount();
        var origin = account.AddTransaction(D(2026, 1, 15), 10m, "old", MonthlyOnDay(), Forever());

        account.ChangeSequenceDescription(origin, D(2026, 1, 15), "new");

        Assert.Equal("new", account.GetMonth(M(2026, 1))[0].Description);
        Assert.Equal("new", account.GetMonth(M(2026, 2))[0].Description);
        Assert.Single(account.GetSequences());
    }

    [Fact]
    public void ChangeSequenceDescription_FromLaterDate_Splits()
    {
        var account = NewAccount();
        account.AddTransaction(D(2026, 1, 15), 10m, "old", MonthlyOnDay(), Forever());

        var march = account.GetMonth(M(2026, 3))[0].Transaction;
        account.ChangeSequenceDescription(march, D(2026, 3, 15), "new");

        Assert.Equal("old", account.GetMonth(M(2026, 2))[0].Description);
        Assert.Equal("new", account.GetMonth(M(2026, 3))[0].Description);
        Assert.Equal(2, account.GetSequences().Count);
    }

    // A whole-series edit passes the sequence origin as the Core handle. When that origin occurrence no longer
    // resolves — e.g. it was individually skipped (tombstoned) — the change must still apply to the sequence;
    // it previously threw "The specified transaction does not exist." (crash on save).
    [Fact]
    public void ChangeSequenceDescription_WhenOriginOccurrenceWasSkipped_StillUpdatesTheSequence()
    {
        var account = NewAccount();
        var origin = account.AddTransaction(D(2026, 1, 10), -20m, "Gym", MonthlyOnDay(), Forever());
        account.RemoveTransaction(origin); // skip (tombstone) the origin occurrence

        account.ChangeSequenceDescription(origin, D(2026, 1, 10), "Fitness");

        var feb = account.GetMonth(M(2026, 2)).Single(e => e.Kind == LedgerEntryKind.Occurrence);
        Assert.Equal("Fitness", feb.Description);
    }

    [Fact]
    public void ChangeSequenceAmount_WhenOriginOccurrenceWasSkipped_StillUpdatesTheSequence()
    {
        var account = NewAccount();
        var origin = account.AddTransaction(D(2026, 1, 10), -20m, "Gym", MonthlyOnDay(), Forever());
        account.RemoveTransaction(origin);

        account.ChangeSequenceAmount(origin, D(2026, 1, 10), -25m);

        var feb = account.GetMonth(M(2026, 2)).Single(e => e.Kind == LedgerEntryKind.Occurrence);
        Assert.Equal(-25m, feb.Amount);
    }
}
