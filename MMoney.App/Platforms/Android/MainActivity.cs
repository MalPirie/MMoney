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

        // Observe every touch-down before any view handles it (clobber-free — base is still called) and notify
        // the TabStrip spike, so a touch cancels its in-flight glide. MAUI surfaces no touch-down for touch
        // (PointerPressed/Entered don't fire on Android), and the Grid's single OnTouchListener is owned by
        // MAUI's own gestures, so the Activity dispatch is the clean place to catch it. Spike-only; a real
        // control would scope this to its own bounds or a custom platform view.
        public override bool DispatchTouchEvent(MotionEvent? e)
        {
            if (e?.ActionMasked == MotionEventActions.Down && IsInsideStrip(e.RawX, e.RawY))
            {
                Components.Sandbox.TabStripSpike.NotifyTouchDown();
            }

            return base.DispatchTouchEvent(e);
        }

        // Scope the touch-down cancel to the strip: true only when the (screen-space) touch lands within the
        // captured strip viewport's bounds. Before the view is captured, nothing is cancelled.
        private static bool IsInsideStrip(float screenX, float screenY)
        {
            if (Components.Sandbox.TabStripSpike.StripPlatformView is not Android.Views.View strip)
            {
                System.Diagnostics.Debug.WriteLine("[TabStripSpike] touch-scope: NO strip view captured yet");
                return false;
            }

            var loc = new int[2];
            strip.GetLocationOnScreen(loc);
            var inside = screenX >= loc[0] && screenX <= loc[0] + strip.Width
                && screenY >= loc[1] && screenY <= loc[1] + strip.Height;
            System.Diagnostics.Debug.WriteLine($"[TabStripSpike] touch-scope x={screenX:F0} y={screenY:F0} rect=({loc[0]},{loc[1]},{strip.Width},{strip.Height}) inside={inside}");
            return inside;
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
