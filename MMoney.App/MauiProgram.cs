using MauiReactor;
using MauiReactor.HotReload;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Hosting;
using MMoney.App.Components;

namespace MMoney.App
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiReactorApp<ShellPage>(app =>
                    {
                        app.UseTheme<MMoneyTheme>();
                    },
                    unhandledExceptionAction: e =>
                    {
                        System.Diagnostics.Debug.WriteLine(e.ExceptionObject);
                    })
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                    fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                    fonts.AddFont("MaterialSymbolsOutlined.ttf", "MaterialSymbols");
                });

            // Capture the TabStrip spike viewport's platform view (keyed by AutomationId) so the Activity can
            // scope its touch-down glide-cancel to the strip's bounds. Spike-only; remove with the spike.
            Microsoft.Maui.Handlers.LayoutHandler.Mapper.AppendToMapping("TabStripViewportCapture", (handler, view) =>
            {
                if ((view as Microsoft.Maui.Controls.VisualElement)?.AutomationId == Components.Sandbox.TabStripSpike.ViewportId)
                {
                    Components.Sandbox.TabStripSpike.StripPlatformView = handler.PlatformView;
                }
            });

            return builder.Build();
        }
    }
}
