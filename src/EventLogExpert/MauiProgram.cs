// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Library.EventResolvers;
using EventLogExpert.Library.Helpers;
using EventLogExpert.Options;
using EventLogExpert.Services;
using EventLogExpert.Store;
using EventLogExpert.Store.EventLog;
using Fluxor;

namespace EventLogExpert;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var fileLocationOptions = new FileLocationOptions();

        // Do this immediately to initialize debug logigng
        var debugLogService = new DebugLogService(fileLocationOptions);
        
        var builder = MauiApp.CreateBuilder();

        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
            });

        builder.Services.AddMauiBlazorWebView();
        builder.Services.AddBlazorWebViewDeveloperTools();

        builder.Services.AddSingleton<ITraceLogger>(debugLogService);

        builder.Services.AddFluxor(options =>
        {
            options.ScanAssemblies(typeof(MauiProgram).Assembly).WithLifetime(StoreLifetime.Singleton);
            options.AddMiddleware<LoggingMiddleware>();
        });

        Directory.CreateDirectory(fileLocationOptions.DatabasePath);

        builder.Services.AddSingleton<IDatabaseCollectionProvider, DatabaseCollectionProvider>();

        builder.Services.AddTransient<IEventResolver, VersatileEventResolver>();

        builder.Services.AddSingleton<ILogWatcherService, LiveLogWatcherService>();

        builder.Services.AddSingleton<ICurrentVersionProvider, CurrentVersionProvider>();

        builder.Services.AddSingleton<IAppTitleService, AppTitleService>();

        builder.Services.AddSingleton<IUpdateService, UpdateService>();

        builder.Services.AddSingleton<FileLocationOptions, FileLocationOptions>();

        return builder.Build();
    }
}
