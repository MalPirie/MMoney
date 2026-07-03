using MMoney.Core.Repeat;
using Xunit;

namespace MMoney.Core.Tests;

/// <summary>
/// Covers the pure <see cref="RepeatDescription"/> formatter — every <see cref="RepeatStrategy"/> and
/// <see cref="RepeatEndCondition"/> branch, including the weekday-set phrasing, the ordinal-day suffixes, and the
/// nth/last-weekday wording. Depends on nothing in the ledger, so it is table-tested directly.
/// </summary>
public class RepeatDescriptionTests
{
    // Thursday, 1 January 2026 — a known weekday (for the nth/last-weekday cases) and day-of-month 1.
    private static readonly DateOnly Origin = new(2026, 1, 1);

    public static IEnumerable<object[]> Strategies() => new[]
    {
        new object[] { new RepeatStrategy.Never(), Origin, "does not repeat" },

        new object[] { new RepeatStrategy.Daily(1), Origin, "every day" },
        new object[] { new RepeatStrategy.Daily(3), Origin, "every 3 days" },

        new object[] { new RepeatStrategy.Weekly(1, DaysOfWeek.All), Origin, "every day" },
        new object[] { new RepeatStrategy.Weekly(1, DaysOfWeek.Weekdays), Origin, "every weekday" },
        new object[] { new RepeatStrategy.Weekly(1, DaysOfWeek.Weekend), Origin, "every weekend day" },
        new object[] { new RepeatStrategy.Weekly(1, DaysOfWeek.Monday), Origin, "every Monday" },
        new object[]
        {
            new RepeatStrategy.Weekly(1, DaysOfWeek.Monday | DaysOfWeek.Wednesday | DaysOfWeek.Friday),
            Origin, "every week on Monday, Wednesday and Friday"
        },
        new object[] { new RepeatStrategy.Weekly(2, DaysOfWeek.Monday), Origin, "every 2 weeks on Monday" },
        new object[]
        {
            new RepeatStrategy.Weekly(2, DaysOfWeek.Monday | DaysOfWeek.Tuesday),
            Origin, "every 2 weeks on Monday and Tuesday"
        },

        new object[] { new RepeatStrategy.Monthly(1, DayInMonth.DayOfMonth), Origin, "every month on the 1st" },
        new object[] { new RepeatStrategy.Monthly(1, DayInMonth.DayOfMonth), new DateOnly(2026, 1, 2), "every month on the 2nd" },
        new object[] { new RepeatStrategy.Monthly(1, DayInMonth.DayOfMonth), new DateOnly(2026, 1, 3), "every month on the 3rd" },
        new object[] { new RepeatStrategy.Monthly(1, DayInMonth.DayOfMonth), new DateOnly(2026, 1, 5), "every month on the 5th" },
        new object[] { new RepeatStrategy.Monthly(1, DayInMonth.DayOfMonth), new DateOnly(2026, 1, 22), "every month on the 22nd" },
        new object[] { new RepeatStrategy.Monthly(3, DayInMonth.DayOfMonth), Origin, "every 3 months on the 1st" },
        new object[] { new RepeatStrategy.Monthly(1, DayInMonth.First), Origin, "every month on the first Thursday" },
        new object[] { new RepeatStrategy.Monthly(1, DayInMonth.Last), Origin, "every month on the last Thursday" },
        new object[] { new RepeatStrategy.Monthly(2, DayInMonth.Second), Origin, "every 2 months on the second Thursday" },

        new object[] { new RepeatStrategy.Yearly(1), Origin, "every year on 1 January" },
        new object[] { new RepeatStrategy.Yearly(3), Origin, "every 3 years on 1 January" },
    };

    [Theory]
    [MemberData(nameof(Strategies))]
    public void Describe_RendersEachStrategy(RepeatStrategy strategy, DateOnly origin, string expected) =>
        Assert.Equal(expected, RepeatDescription.Describe(strategy, origin));

    public static IEnumerable<object[]> EndConditions() => new[]
    {
        new object[] { new RepeatEndCondition.Forever(), null!, string.Empty },
        new object[] { new RepeatEndCondition.UntilDate(new DateOnly(2027, 1, 1)), null!, ", until 1 Jan 2027" },
        new object[]
        {
            new RepeatEndCondition.AfterOccurrences(12), (DateOnly?)new DateOnly(2027, 6, 15),
            ", 12 occurrences (ends 15 Jun 2027)"
        },
        new object[] { new RepeatEndCondition.AfterOccurrences(12), null!, ", 12 occurrences" },
    };

    [Theory]
    [MemberData(nameof(EndConditions))]
    public void DescribeEndCondition_RendersEachEnd(RepeatEndCondition end, DateOnly? endDate, string expected) =>
        Assert.Equal(expected, RepeatDescription.DescribeEndCondition(end, endDate));
}
