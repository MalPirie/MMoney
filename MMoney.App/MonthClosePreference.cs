using Microsoft.Maui.Storage;

namespace MMoney.App;

/// <summary>
/// The persisted "allow months to be closed" preference. When <see cref="Allowed"/>, <see cref="AccountEvent"/>
/// month-close events are fully processed — a closed month collapses into a "Balance carried" anchor — and the
/// ledger offers a close action on the oldest open month. When not allowed, closes are only partially processed
/// (sequences pruned, the edit lock advanced) so past months stay visible but read-only, and no close is offered.
/// This is the inverse of the Core's <c>ignoreMonthClosed</c> flag: <c>ignoreMonthClosed = !Allowed</c>.
/// </summary>
public static class MonthClosePreference
{
    private const string Key = "allow_month_close";

    /// <summary>Whether months may be closed (and closes collapse into a carried balance). Defaults to <see langword="false"/>.</summary>
    public static bool Allowed
    {
        get => Preferences.Default.Get(Key, false);
        set => Preferences.Default.Set(Key, value);
    }
}
