using System;
using MMoney.App.Input;
using Xunit;

namespace MMoney.App.Tests;

/// <summary>The pure mask/parse/format logic behind the transaction date field (ADR-0006).</summary>
public class DateEntryTests
{
    // ── mask ──────────────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("", "")]
    [InlineData("6", "6")]
    [InlineData("06", "06")]
    [InlineData("060", "06/0")]
    [InlineData("0607", "06/07")]
    [InlineData("060720", "06/07/20")]
    [InlineData("06072026", "06/07/2026")]
    public void Mask_GroupsDigitsProgressively(string raw, string expected)
    {
        Assert.Equal(expected, DateEntry.Mask(raw));
    }

    [Fact]
    public void Mask_CapsAtEightDigits()
    {
        Assert.Equal("06/07/2026", DateEntry.Mask("0607202699"));
    }

    [Fact]
    public void Mask_StripsNonDigits()
    {
        Assert.Equal("06/07/2026", DateEntry.Mask("06/07/2026"));
        Assert.Equal("6", DateEntry.Mask("ab6"));
    }

    // ── parse ─────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TryParse_ReadsACompleteMaskedDate()
    {
        Assert.True(DateEntry.TryParse("06/07/2026", out var date));
        Assert.Equal(new DateOnly(2026, 7, 6), date);
    }

    [Fact]
    public void TryParse_AcceptsBareDigits()
    {
        Assert.True(DateEntry.TryParse("06072026", out var date));
        Assert.Equal(new DateOnly(2026, 7, 6), date);
    }

    [Theory]
    [InlineData("")]
    [InlineData("06/07/20")]   // incomplete (six digits)
    [InlineData("31/02/2026")] // impossible day
    [InlineData("00/07/2026")] // day zero
    [InlineData("06/13/2026")] // month thirteen
    public void TryParse_RejectsIncompleteOrImpossible(string text)
    {
        Assert.False(DateEntry.TryParse(text, out _));
    }

    // ── format ────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Format_IsZeroPaddedDayMonthYear()
    {
        Assert.Equal("06/07/2026", DateEntry.Format(new DateOnly(2026, 7, 6)));
    }

    [Fact]
    public void Weekday_IsTheFullDayName()
    {
        Assert.Equal("Monday", DateEntry.Weekday(new DateOnly(2026, 7, 6)));
    }
}
