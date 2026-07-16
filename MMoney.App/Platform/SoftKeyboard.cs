namespace MMoney.App.Platform;

/// <summary>
/// Dismisses the Android soft keyboard and clears input focus. Used when a non-text control (a combo, day chip,
/// radio, or picker) is engaged, so a still-focused entry doesn't keep the keyboard up or its focus highlight lit.
/// </summary>
public static class SoftKeyboard
{
    /// <summary>Hide the soft keyboard and clear focus; returns whether anything was focused (the keyboard was up).</summary>
    public static bool Hide()
    {
#if ANDROID
        var activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;
        var focus = activity?.CurrentFocus;
        if (activity is not null && focus is not null)
        {
            var imm = (Android.Views.InputMethods.InputMethodManager?)
                activity.GetSystemService(Android.Content.Context.InputMethodService);
            imm?.HideSoftInputFromWindow(focus.WindowToken, Android.Views.InputMethods.HideSoftInputFlags.None);
            focus.ClearFocus();
            return true;
        }
#endif
        return false;
    }
}
