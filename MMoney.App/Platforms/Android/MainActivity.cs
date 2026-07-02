using System;
using Android.App;
using Android.Content.PM;
using Android.Content.Res;
using Android.OS;
using Android.Views;
using Microsoft.Maui.ApplicationModel;

namespace MMoney.App
{
    [Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
    public class MainActivity : MauiAppCompatActivity
    {
        protected override void OnCreate(Bundle? savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            ApplySystemBarChrome();

            // Re-apply when the effective app theme changes at runtime (e.g. the in-app theme toggle).
            if (Microsoft.Maui.Controls.Application.Current is { } app)
            {
                app.RequestedThemeChanged += (_, _) => ApplySystemBarChrome();
            }
        }

        public override void OnConfigurationChanged(Configuration newConfig)
        {
            base.OnConfigurationChanged(newConfig);
            ApplySystemBarChrome();
        }

        // Colour the system bars to match the app chrome, adapting to the active theme:
        //   status bar     = brand primary       (deep-orange + light icons / light-orange + dark icons)
        //   navigation bar = surfaceContainer     (matching the in-app bottom nav bar)
        //
        // Uses the broad-compatibility system-bar APIs (deprecated on newer Android). A WindowInsetsController
        // based helper in Mobiorum.Material3 is the planned replacement, to land with the dark-mode chrome review.
#pragma warning disable CA1422 // deprecated system-bar APIs; intentional for broad device support, see note above
        private void ApplySystemBarChrome()
        {
            if (Window is null)
            {
                return;
            }

            var isDark = IsEffectiveDarkTheme();
            Window.SetStatusBarColor(Android.Graphics.Color.ParseColor(isDark ? "#FFB77C" : "#B35A00"));
            Window.SetNavigationBarColor(Android.Graphics.Color.ParseColor(isDark ? "#271C15" : "#F6EAE0"));

            // Light status-bar icons (the dark-icon flag) arrived in API 23; the nav-bar equivalent in API 26.
            if (OperatingSystem.IsAndroidVersionAtLeast(23))
            {
                var decor = Window.DecorView;
                var flags = decor.SystemUiFlags;

                // Status bar rides the orange primary: light icons in light mode, dark icons in dark mode.
                flags = isDark ? flags | SystemUiFlags.LightStatusBar : flags & ~SystemUiFlags.LightStatusBar;

                // Nav bar rides surfaceContainer: dark icons on the light surface, light icons on the dark one.
                if (OperatingSystem.IsAndroidVersionAtLeast(26))
                {
                    flags = isDark ? flags & ~SystemUiFlags.LightNavigationBar : flags | SystemUiFlags.LightNavigationBar;
                }

                decor.SystemUiFlags = flags;
            }
        }
#pragma warning restore CA1422

        // The effective theme (the in-app override if set, otherwise the system theme).
        private static bool IsEffectiveDarkTheme() =>
            Microsoft.Maui.Controls.Application.Current?.RequestedTheme == AppTheme.Dark;
    }
}
