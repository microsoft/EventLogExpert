// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Helpers;
using EventLogExpert.UI.Options;
using EventLogExpert.UI.Services;
using EventLogExpert.UI.Store;
using EventLogExpert.UI.Store.EventLog;
using Fluxor;

namespace EventLogExpert.Services;

public static class CoreServices
{
    public static IServiceCollection AddCoreServices(this IServiceCollection services)
    {
        services.AddMauiBlazorWebView();
        services.AddBlazorWebViewDeveloperTools();

        services.AddSingleton<ITraceLogger, DebugLogService>();

        services.AddFluxor(options =>
        {
            options.ScanAssemblies(typeof(MauiProgram).Assembly).WithLifetime(StoreLifetime.Singleton);
            options.AddMiddleware<LoggingMiddleware>();
        });

        services.AddSingleton<ILogWatcherService, LiveLogWatcherService>();

        var fileLocationOptions = new FileLocationOptions(FileSystem.AppDataDirectory);
        services.AddSingleton(fileLocationOptions);

        Directory.CreateDirectory(fileLocationOptions.DatabasePath);

        return services;
    }
}
