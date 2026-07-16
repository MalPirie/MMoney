using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;

namespace Mobiorum.Material3;

/// <summary>
/// On-screen geometry of a laid-out native control, for anchoring overlays to a field (e.g. the
/// <see cref="ComboBox"/> dropdown under its trigger). Uses the platform view captured via a MauiReactor
/// native-control ref; returns device-independent units. Android-only for now (the app's target).
/// </summary>
public static class NativeGeometry
{
    /// <summary>The control's on-screen rectangle in DIPs, or null if it has no laid-out platform view.</summary>
    public static Rect? ScreenRect(VisualElement? view)
    {
#if ANDROID
        if (view?.Handler?.PlatformView is Android.Views.View native && native.Width > 0 && native.Height > 0)
        {
            var location = new int[2];
            native.GetLocationOnScreen(location);
            var density = native.Context?.Resources?.DisplayMetrics?.Density ?? 1f;
            return new Rect(location[0] / density, location[1] / density, native.Width / density, native.Height / density);
        }
#endif
        return null;
    }
}
