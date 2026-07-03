using MMoney.Core;
using Xunit;
using static MMoney.Core.Tests.TestHelpers;

namespace MMoney.Core.Tests;

/// <summary>
/// Guards the per-account sequence-number allocation directly, at the public surface. The mint is fused to the
/// emit in <see cref="Account"/>, so a decomposed command that adds a sequence always consumes a fresh number;
/// these assert that invariant instead of leaving it only implicitly covered by a single replay test.
/// </summary>
public class SequenceNumberTests
{
    [Fact]
    public void DecomposedCommands_MintFreshDistinctNumbers_NeverColliding()
    {
        var account = NewAccount();
        account.AddTransaction(D(2026, 1, 15), 10m, "rent", MonthlyOnDay(), Forever()); // number 1

        // A later-dated amount change splits the sequence: truncated original + a new one (mints a number).
        var march = account.GetMonth(M(2026, 3))[0].Transaction;
        account.ChangeSequenceAmount(march, D(2026, 3, 15), 12m);

        // A later-dated date change on the new series splits again (mints another).
        var may = account.GetMonth(M(2026, 5))[0].Transaction;
        account.ChangeSequenceDate(may, D(2026, 5, 15), D(2026, 5, 20));

        var numbers = account.GetSequences().Select(s => s.Number).ToList();
        Assert.Equal(3, numbers.Count);
        Assert.Equal(numbers.Count, numbers.Distinct().Count());        // no collision
        Assert.Equal(new[] { 1, 2, 3 }, numbers.OrderBy(n => n));        // strictly increasing mint
    }

    [Fact]
    public void MintedNumbers_SurviveReplayIdentically()
    {
        var (account, events) = Recording();
        account.AddTransaction(D(2026, 1, 15), 10m, "rent", MonthlyOnDay(), Forever());
        var march = account.GetMonth(M(2026, 3))[0].Transaction;
        account.ChangeSequenceAmount(march, D(2026, 3, 15), 12m);

        var original = account.GetSequences().Select(s => s.Number).OrderBy(n => n).ToList();

        var replayed = new Account(Guid.NewGuid(), events);
        var afterReplay = replayed.GetSequences().Select(s => s.Number).OrderBy(n => n).ToList();

        Assert.Equal(original, afterReplay);
    }
}
