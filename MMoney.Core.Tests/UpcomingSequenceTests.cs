using MMoney.Core;
using MMoney.Core.Repeat;
using Xunit;
using static MMoney.Core.Tests.TestHelpers;

namespace MMoney.Core.Tests;

public class UpcomingSequenceTests
{
    [Fact]
    public void GetUpcomingSequences_OrdersByNextDueAndReportsIt()
    {
        var account = NewAccount();
        account.AddTransaction(D(2026, 1, 5), 10m, "fifth", MonthlyOnDay(), Forever());       // next after 03-10 = 04-05
        account.AddTransaction(D(2026, 1, 20), 20m, "twentieth", MonthlyOnDay(), Forever());  // next = 03-20
        account.AddTransaction(D(2026, 1, 1), 5m, "daily", new RepeatStrategy.Daily(1), Forever()); // next = 03-10

        var upcoming = account.GetUpcomingSequences(D(2026, 3, 10));

        Assert.Equal(new[] { "daily", "twentieth", "fifth" }, upcoming.Select(u => u.Sequence.Description).ToArray());
        Assert.Equal(new[] { D(2026, 3, 10), D(2026, 3, 20), D(2026, 4, 5) }, upcoming.Select(u => u.NextDue).ToArray());
    }

    [Fact]
    public void GetUpcomingSequences_ExcludesSequencesWithNoUpcomingOccurrence()
    {
        var account = NewAccount();
        account.AddTransaction(D(2026, 1, 15), 10m, "ended", MonthlyOnDay(), new RepeatEndCondition.UntilDate(D(2026, 2, 28)));
        account.AddTransaction(D(2026, 1, 20), 20m, "ongoing", MonthlyOnDay(), Forever());

        var upcoming = account.GetUpcomingSequences(D(2026, 3, 10));

        Assert.Equal(new[] { "ongoing" }, upcoming.Select(u => u.Sequence.Description).ToArray());
        Assert.Equal(D(2026, 3, 20), upcoming[0].NextDue);
    }

    [Fact]
    public void GetUpcomingSequences_ExcludesSequencesCompletedBeforeTheEditLock()
    {
        var account = NewAccount();
        account.AddTransaction(D(2026, 1, 15), 10m, "ending", MonthlyOnDay(), new RepeatEndCondition.UntilDate(D(2026, 3, 31)));
        account.AddTransaction(D(2026, 1, 20), 20m, "ongoing", MonthlyOnDay(), Forever());
        account.AddTransaction(D(2026, 4, 10), 5m, "anchor-helper");

        account.CloseMonth(M(2026, 1), D(2026, 5, 15));
        account.CloseMonth(M(2026, 2), D(2026, 5, 15));
        account.CloseMonth(M(2026, 3), D(2026, 5, 15)); // edit lock = 2026-04-01; "ending" is now completed

        var upcoming = account.GetUpcomingSequences(D(2026, 5, 1));

        Assert.Equal(new[] { "ongoing" }, upcoming.Select(u => u.Sequence.Description).ToArray());
        Assert.Equal(D(2026, 5, 20), upcoming[0].NextDue);
    }

    [Fact]
    public void GetUpcomingSequences_IsEmptyWhenThereAreNoSequences()
    {
        var account = NewAccount();
        account.AddTransaction(D(2026, 1, 10), 100m, "one-off");

        Assert.Empty(account.GetUpcomingSequences(D(2026, 1, 1)));
    }
}
