using MauiReactor;
using MauiReactor.HotReload;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Hosting;
using Mobiorum.Material3;
using MMoney.App.Components;
using MMoney.Core;

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

            // The event-sourced ledger, loaded from (and persisted to) the app-data directory. A singleton — one
            // live account collection for the app's lifetime; the shell resolves it and the Core owns all state.
            builder.Services.AddSingleton(_ =>
            {
                var directory = System.IO.Path.Combine(
                    Microsoft.Maui.Storage.FileSystem.AppDataDirectory, "accounts");
                var fileSystem = new System.IO.Abstractions.FileSystem();
                fileSystem.Directory.CreateDirectory(directory); // first run: the manager reads the dir, doesn't create it
                return new AccountManager(
                    new AccountPersistenceService(directory, fileSystem),
                    ignoreMonthClosed: false,
                    System.TimeProvider.System);
            });

            return builder.Build();
        }
    }
}
