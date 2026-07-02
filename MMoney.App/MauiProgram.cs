using MauiReactor;
using MauiReactor.HotReload;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Hosting;
using Mobiorum.Material3;
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
                .UseMobiorumMaterial3() // registers the library's custom control handlers (TabStrip touch-down seam)
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                    fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                    fonts.AddFont("MaterialSymbolsOutlined.ttf", "MaterialSymbols");
                });

            return builder.Build();
        }
    }
}
