using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using MMoney.Core;

namespace MMoney.App.Export;

/// <summary>
/// Renders whole-account ledger rows as a printable HTML statement — a titled table of Date, Description, Amount,
/// Balance. Pure and deterministic, so it is headless-tested; the platform print step (a WebView driving Android's
/// print framework) lives in <see cref="MMoney.App.Platform.LedgerPrinter"/>. Shares the ledger rows with the CSV
/// export (see <see cref="LedgerExport.CollectRows"/>); only the presentation differs — human-readable here (en-GB
/// currency, long dates) versus machine-readable there.
/// </summary>
public static class LedgerHtml
{
    private static readonly CultureInfo Gb = CultureInfo.GetCultureInfo("en-GB");

    /// <summary>
    /// Builds the statement document for <paramref name="rows"/>, headed by <paramref name="title"/> (the account
    /// name; a blank falls back to "Account") and the <paramref name="today"/> as-of date. Amounts use en-GB
    /// currency to match the app's on-screen formatting (the sign alone marks debits — black and white only, so it
    /// prints cleanly on a monochrome printer); descriptions are HTML-escaped.
    /// </summary>
    public static string Build(IReadOnlyList<LedgerEntry> rows, string title, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(rows);

        var sb = new StringBuilder();
        sb.Append("<!DOCTYPE html><html><head><meta charset=\"utf-8\"><style>");
        sb.Append("body{font-family:sans-serif;margin:24px;color:#000}");
        sb.Append("h1{font-size:18px;margin:0 0 2px}");
        sb.Append(".sub{color:#555;font-size:12px;margin-bottom:16px}");
        sb.Append("table{width:100%;border-collapse:collapse;font-size:12px}");
        sb.Append("th,td{text-align:left;padding:6px 8px;border-bottom:1px solid #ddd}");
        sb.Append("th.num,td.num{text-align:right;white-space:nowrap}");
        sb.Append("</style></head><body>");

        sb.Append("<h1>").Append(Escape(string.IsNullOrWhiteSpace(title) ? "Account" : title)).Append("</h1>");
        sb.Append("<div class=\"sub\">Statement as of ").Append(today.ToString("d MMMM yyyy", Gb)).Append("</div>");

        sb.Append("<table><thead><tr><th>Date</th><th>Description</th>");
        sb.Append("<th class=\"num\">Amount</th><th class=\"num\">Balance</th></tr></thead><tbody>");
        foreach (var row in rows)
        {
            sb.Append("<tr><td>").Append(row.Date.ToString("dd MMM yyyy", Gb)).Append("</td>");
            sb.Append("<td>").Append(Escape(row.Description)).Append("</td>");
            sb.Append("<td class=\"num\">").Append(row.Amount.ToString("C", Gb)).Append("</td>");
            sb.Append("<td class=\"num\">").Append(row.Balance.ToString("C", Gb)).Append("</td></tr>");
        }

        sb.Append("</tbody></table></body></html>");
        return sb.ToString();
    }

    // Escape the five characters that matter in HTML text/attribute content; & first so it doesn't double-escape
    // the entities introduced by the later replacements.
    private static string Escape(string text) => text
        .Replace("&", "&amp;")
        .Replace("<", "&lt;")
        .Replace(">", "&gt;")
        .Replace("\"", "&quot;");
}
