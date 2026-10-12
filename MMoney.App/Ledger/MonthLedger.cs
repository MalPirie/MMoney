using System;
using System.Collections.Generic;
using System.Linq;
using MMoney.Core;

namespace MMoney.App.Ledger;

/// <summary>One day's ledger rows in display order (latest sequence first).</summary>
public sealed record LedgerDay(DateOnly Date, IReadOnlyList<LedgerEntry> Entries);

/// <summary>
/// The month-ledger presentation transform (app-side, per <c>docs/app-design.md</c> §5): turns Core's ascending
/// <see cref="LedgerEntry"/> output into the day-grouped, descending view the screen shows — latest day first,
/// latest sequence first within a day, and the carried-balance anchor (sequence 0, earliest day) last. Balances
/// are left exactly as Core computed them ascending (correct); only the display order is reversed. Pure and
/// MauiReactor-free, so it is unit-tested headlessly in <c>MMoney.App.Tests</c>.
/// </summary>
public static class MonthLedger
{
    /// <summary>Group <paramref name="ascending"/> (Core's <c>GetMonth</c> output) into days, newest first.</summary>
    public static IReadOnlyList<LedgerDay> ByDayDescending(IReadOnlyList<LedgerEntry> ascending) =>
        ascending
            .GroupBy(entry => entry.Date)
            .OrderByDescending(day => day.Key)
            .Select(day => new LedgerDay(
                day.Key,
                day.OrderByDescending(entry => entry.Transaction.Sequence).ToList()))
            .ToList();

    /// <summary>
    /// Ensures <paramref name="daysDescending"/> (as from <see cref="ByDayDescending"/>) has a day for
    /// <paramref name="today"/>, inserting an empty one at the correct chronological position if nothing fell
    /// on it. Used for the current month only, so today's day — with or without transactions — always has a
    /// slot the screen can highlight.
    /// </summary>
    public static IReadOnlyList<LedgerDay> WithToday(IReadOnlyList<LedgerDay> daysDescending, DateOnly today)
    {
        if (daysDescending.Any(day => day.Date == today))
        {
            return daysDescending;
        }

        var result = new List<LedgerDay>();
        var inserted = false;

        foreach (var day in daysDescending)
        {
            if (!inserted && day.Date < today)
            {
                result.Add(new LedgerDay(today, []));
                inserted = true;
            }

            result.Add(day);
        }

        if (!inserted)
        {
            result.Add(new LedgerDay(today, []));
        }

        return result;
    }
}
