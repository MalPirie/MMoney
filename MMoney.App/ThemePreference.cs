using System;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Storage;

namespace MMoney.App;

/// <summary>
/// The single source of truth for the app's tristate theme preference (System / Light / Dark). Persists the
/// choice in <see cref="Preferences"/> and applies it through <see cref="MauiControls.Application.UserAppTheme"/>.
/// The shell loads it once on mount; the Settings page reads <see cref="Current"/> to highlight the active
/// option and calls <see cref="Set"/> to change it.
/// </summary>
public static class ThemePreference
{
    private const string Key = "app_theme";

    /// <summary>The persisted preference, defaulting to <see cref="AppTheme.Unspecified"/> (follow the system).</summary>
    public static AppTheme Current =>
        Enum.TryParse<AppTheme>(Preferences.Default.Get(Key, nameof(AppTheme.Unspecified)), out var theme)
            ? theme
            : AppTheme.Unspecified;

    /// <summary>Applies the persisted preference to the running app. Call once on startup.</summary>
    public static void Load()
    {
        if (MauiControls.Application.Current is { } app)
        {
            app.UserAppTheme = Current;
        }
    }

    /// <summary>Persists and immediately applies a new theme preference.</summary>
    public static void Set(AppTheme theme)
    {
        Preferences.Default.Set(Key, theme.ToString());
        if (MauiControls.Application.Current is { } app)
        {
            app.UserAppTheme = theme;
        }
    }
}
