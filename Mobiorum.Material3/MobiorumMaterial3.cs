using Microsoft.Maui.Hosting;

namespace Mobiorum.Material3;

/// <summary>
/// Registration entry point for the Mobiorum Material 3 control library. Call <see cref="UseMobiorumMaterial3"/>
/// from <c>MauiProgram</c> so the library's custom controls get their platform handlers (currently the
/// <see cref="TouchDownContentView"/> touch-down observer that lets <see cref="TabStrip{TItem}"/> cancel a fling
/// on first contact).
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

        return builder;
    }
}
