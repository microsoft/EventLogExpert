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

        Directory.CreateDirectory(Utils.DatabasePath);

        var dbResolver = new EventProviderDatabaseEventResolver(Utils.Trace);
        var localResolver = new LocalProviderEventResolver(Utils.Trace);
        var versEventResolver = new VersatileEventResolver(localResolver, dbResolver, Utils.Trace);

        builder.Services.AddSingleton<IEventResolver>(versEventResolver);

        builder.Services.AddSingleton<ILogWatcherService, LiveLogWatcher>();

        return builder.Build();
    }
}
