using System;
using System.Linq;
using MMoney.App.Repeat;
using MMoney.Core.Repeat;
using Xunit;

namespace MMoney.App.Tests;

/// <summary>The pure Monthly-options / preset / coercion logic behind the §8 repeat picker (ADR-0007).</summary>
public class RepeatEditingTests
{
    // ── Monthly options ───────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void MonthlyOptions_MidMonth_OffersDayOfMonthAndTheNthWeekday()
    {
        // Mon 6 Jul 2026: the 6th → first Monday (nth 1), not the last (13th is another Monday).
        var options = RepeatEditing.MonthlyOptions(new DateOnly(2026, 7, 6));
        Assert.Equal(new[] { DayInMonth.DayOfMonth, DayInMonth.First }, options);
    }

    [Fact]
    public void MonthlyOptions_LastWeekOfMonth_AlsoOffersLast()
    {
        // Mon 27 Jul 2026: nth = (27-1)/7+1 = 4, and 27+7 > 31 → also the last Monday.
        var options = RepeatEditing.MonthlyOptions(new DateOnly(2026, 7, 27));
        Assert.Equal(new[] { DayInMonth.DayOfMonth, DayInMonth.Fourth, DayInMonth.Last }, options);
    }

    [Fact]
    public void MonthlyOptions_FifthOccurrence_OffersOnlyLastNotAnNth()
    {
        // Thu 29 Jan 2026: nth = (29-1)/7+1 = 5 (> Fourth), and it's the last Thursday → Last only.
        var options = RepeatEditing.MonthlyOptions(new DateOnly(2026, 1, 29));
        Assert.Equal(new[] { DayInMonth.DayOfMonth, DayInMonth.Last }, options);
    }

    // ── presets ───────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void EveryWeek_DefaultsToTheOriginsWeekday()
    {
        var weekly = Assert.IsType<RepeatStrategy.Weekly>(RepeatEditing.EveryWeek(new DateOnly(2026, 7, 6)));
        Assert.Equal(1, weekly.Interval);
        Assert.Equal(DaysOfWeek.Monday, weekly.Days);
    }

    [Fact]
    public void EveryMonth_DefaultsToDayOfMonth()
    {
        var monthly = Assert.IsType<RepeatStrategy.Monthly>(RepeatEditing.EveryMonth());
        Assert.Equal(1, monthly.Interval);
        Assert.Equal(DayInMonth.DayOfMonth, monthly.DayInMonth);
    }

    [Theory]
    [InlineData(DayOfWeek.Sunday, DaysOfWeek.Sunday)]
    [InlineData(DayOfWeek.Friday, DaysOfWeek.Friday)]
    public void ToDaysOfWeek_MapsSystemDayToFlag(DayOfWeek day, DaysOfWeek expected)
    {
        Assert.Equal(expected, RepeatEditing.ToDaysOfWeek(day));
    }

    // ── number coercion ───────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("3", 3)]
    [InlineData("", 1)]
    [InlineData("abc", 1)]
    [InlineData("0", 1)]
    [InlineData("-4", 1)]
    public void CoerceCount_FallsBackToOne(string text, int expected)
    {
        Assert.Equal(expected, RepeatEditing.CoerceCount(text));
    }
}
