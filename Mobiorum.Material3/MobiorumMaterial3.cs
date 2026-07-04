using Microsoft.Maui.Hosting;

namespace Mobiorum.Material3;

/// <summary>
/// Registration entry point for the Mobiorum Material 3 control library. Call <see cref="UseMobiorumMaterial3"/>
/// from <c>MauiProgram</c> so the library's custom controls get their platform handlers: the
/// <see cref="TouchDownContentView"/> touch-down observer (lets <see cref="TabStrip{TItem}"/> cancel a fling on
/// first contact) and the <see cref="CarouselSettleObserver"/> scroll-settle hook (lets
/// <see cref="TabbedPageView{TItem}"/> commit its selection only when the body settles).
/// </summary>
public static class MobiorumMaterial3
{
    public static MauiAppBuilder UseMobiorumMaterial3(this MauiAppBuilder builder)
    {
        builder.ConfigureMauiHandlers(handlers =>
        {
#if ANDROID
            handlers.AddHandler<TouchDownContentView, TouchDownContentViewHandler>();
#else
            // Other platforms: no touch-down observer needed — the stock handler renders the child, and the
            // fling still cancels on pan-start / tap.
            handlers.AddHandler<TouchDownContentView, Microsoft.Maui.Handlers.ContentViewHandler>();
#endif
        });

        CarouselSettleObserver.Install(); // adds the CarouselView scroll-settle hook (Android; no-op elsewhere)

#if ANDROID
        // Strip the native underline from text inputs so they can live inside our M3 filled fields (which draw
        // their own active indicator). Without this the platform underline doubles up with ours. The Android
        // Entry and DatePicker both back onto an EditText, so both need it.
        var transparent = Android.Content.Res.ColorStateList.ValueOf(Android.Graphics.Color.Transparent);
        Microsoft.Maui.Handlers.EntryHandler.Mapper.AppendToMapping("MobiorumNoUnderline", (handler, _) =>
            handler.PlatformView.BackgroundTintList = transparent);
        Microsoft.Maui.Handlers.DatePickerHandler.Mapper.AppendToMapping("MobiorumNoUnderline", (handler, _) =>
            handler.PlatformView.BackgroundTintList = transparent);
#endif

        return builder;
    }
}
