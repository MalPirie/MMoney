using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using MMoney.Core;

namespace MMoney.App.Export;

/// <summary>
/// Builds a whole-account CSV statement from the ledger. Pure and deterministic, so it is headless-tested: the
/// caller supplies the account and today, this collects the month-by-month rows and formats them as RFC 4180 CSV.
/// The share/write side (a platform file + share sheet) stays in the component.
/// </summary>
public static class LedgerExport
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    /// <summary>
    /// The whole-account ledger rows — the first content month through <paramref name="today"/>'s month inclusive,
    /// in chronological order, each carrying its running balance. The lower bound is
    /// <see cref="Account.EarliestContentMonth"/>, which already reflects the account's month-close mode: when
    /// closing months is allowed the closed history is collapsed, so this is the first <em>open</em> month (its
    /// carried-balance anchor); when it is not, closed months remain visible, so this is the true first month. That
    /// makes "from the first open month / from all months" fall out of the account itself — no separate scope
    /// switch. Occurrences project infinitely forward, so the current month is the upper bound. Empty when the
    /// account has no content, or when today precedes the first content month.
    /// </summary>
    public static IReadOnlyList<LedgerEntry> CollectRows(Account account, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(account);

        if (account.EarliestContentMonth() is not { } start)
        {
            return [];
        }

        var end = MonthOnly.FromDate(today);
        if (end.CompareTo(start) < 0)
        {
            return [];
        }

        var rows = new List<LedgerEntry>();
        foreach (var month in start.To(end))
        {
            rows.AddRange(account.GetMonth(month));
        }

        return rows;
    }

    /// <summary>
    /// Formats ledger rows as RFC 4180 CSV with a header line — columns Date (ISO 8601), Description, Amount,
    /// Balance. Amount and Balance are signed, two-decimal, invariant-culture with no currency symbol, so the file
    /// round-trips into a spreadsheet regardless of locale. Uses CRLF line endings per the CSV spec.
    /// </summary>
    public static string ToCsv(IReadOnlyList<LedgerEntry> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);

        var sb = new StringBuilder();
        sb.Append("Date,Description,Amount,Balance\r\n");
        foreach (var row in rows)
        {
            sb.Append(row.Date.ToString("yyyy-MM-dd", Invariant));
            sb.Append(',');
            sb.Append(Escape(row.Description));
            sb.Append(',');
            sb.Append(row.Amount.ToString("0.00", Invariant));
            sb.Append(',');
            sb.Append(row.Balance.ToString("0.00", Invariant));
            sb.Append("\r\n");
        }

        return sb.ToString();
    }

    // RFC 4180: a field is quoted only when it contains a comma, a double-quote, or a line break; any inner
    // double-quotes are doubled. Descriptions are free text, so this is the only field that can need it.
    private static string Escape(string field) =>
        field.IndexOfAny([',', '"', '\r', '\n']) < 0
            ? field
            : string.Concat("\"", field.Replace("\"", "\"\""), "\"");
}
