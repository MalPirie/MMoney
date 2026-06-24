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


            return builder.Build();
        }
    }
}
