using System;
using System.Globalization;
using System.Linq;

namespace MMoney.App.Input;

/// <summary>
/// The pure text logic behind the transaction date field (ADR-0006): a deterministic <c>dd/MM/yyyy</c>
/// digits-model mask, an exact parse of a complete date, and the canonical display/weekday formats. Input is
/// numeric-only and auto-masked, so a committed value is always eight digits parsed exactly (which also rejects
/// impossible dates like 31/02). MauiReactor-free, so it is unit-tested headlessly in <c>MMoney.App.Tests</c>.
/// </summary>
public static class DateEntry
{
    private static readonly CultureInfo Gb = CultureInfo.GetCultureInfo("en-GB");

    /// <summary>Reformat raw input to a progressive <c>dd/MM/yyyy</c> mask: keep up to eight digits, group them
    /// day/month/year, inserting the slashes. Non-digits (including any the user pastes) are dropped.</summary>
    public static string Mask(string raw)
    {
        var digits = Digits(raw);
        if (digits.Length > 8)
        {
            digits = digits[..8];
        }

        return digits.Length switch
        {
            <= 2 => digits,
            <= 4 => $"{digits[..2]}/{digits[2..]}",
            _ => $"{digits[..2]}/{digits[2..4]}/{digits[4..]}",
        };
    }

    /// <summary>Parse a complete date (eight digits, <c>dd/MM/yyyy</c>) exactly; false while incomplete or for an
    /// impossible date. The exact parse validates day/month ranges, so 31/02/2026 is rejected.</summary>
    public static bool TryParse(string text, out DateOnly date)
    {
        var digits = Digits(text);
        if (digits.Length == 8 &&
            DateTime.TryParseExact(digits, "ddMMyyyy", Gb, DateTimeStyles.None, out var parsed))
        {
            date = DateOnly.FromDateTime(parsed);
            return true;
        }

        date = default;
        return false;
    }

    /// <summary>The canonical <c>dd/MM/yyyy</c> display string.</summary>
    public static string Format(DateOnly date) => date.ToString("dd/MM/yyyy", Gb);

    /// <summary>The full weekday name (e.g. "Monday"), shown as the field's supporting hint.</summary>
    public static string Weekday(DateOnly date) => date.ToString("dddd", Gb);

    private static string Digits(string text) => new(text.Where(char.IsDigit).ToArray());
}
