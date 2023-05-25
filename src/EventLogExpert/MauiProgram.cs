// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Library.EventResolvers;
using EventLogExpert.Library.Helpers;
using EventLogExpert.Store;
using EventLogExpert.Store.EventLog;
using Fluxor;

namespace EventLogExpert;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        Utils.InitTracing();

        var builder = MauiApp.CreateBuilder();

        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
            });

        builder.Services.AddMauiBlazorWebView();
        builder.Services.AddBlazorWebViewDeveloperTools();

        builder.Services.AddSingleton<ITraceLogger>(new DebugLogger(Utils.Trace));

        builder.Services.AddFluxor(options =>
        {
            options.ScanAssemblies(typeof(MauiProgram).Assembly).WithLifetime(StoreLifetime.Singleton);
            options.AddMiddleware<LoggingMiddleware>();
        });

        if (Utils.HasProviderDatabases())
        {
            builder.Services.AddSingleton<IEventResolver>(
                new EventProviderDatabaseEventResolver(Utils.DatabasePath, Utils.Trace));
        }
        else
        {
            builder.Services.AddSingleton<IEventResolver, LocalProviderEventResolver>();
        }

        builder.Services.AddSingleton<ILogWatcherService, LiveLogWatcher>();

        return builder.Build();
    }
}
