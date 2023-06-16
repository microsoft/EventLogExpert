// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Options;
using EventLogExpert.UI.Services;
using EventLogExpert.UI.Store;
using EventLogExpert.UI.Store.EventLog;
using Fluxor;
using EventLogExpert.Services;
using EventLogExpert.UI.Interfaces;
using EventLogExpert.Eventing.EventResolvers;
using EventLogExpert.Eventing.Helpers;

namespace EventLogExpert;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var fileLocationOptions = new FileLocationOptions(FileSystem.AppDataDirectory);

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

        builder.Services.AddSingleton<ITitleProvider, TitleProvider>();

        builder.Services.AddSingleton<IAppTitleService, AppTitleService>();

        builder.Services.AddSingleton<IUpdateService, UpdateService>();

        builder.Services.AddSingleton<IGitHubService, GitHubService>();

        builder.Services.AddSingleton<IDeploymentService, DeploymentService>();

        builder.Services.AddSingleton<FileLocationOptions>(fileLocationOptions);

        builder.Services.AddSingleton<IMainThreadService>(new MainThreadService(MainThread.InvokeOnMainThreadAsync));

        builder.Services.AddSingleton<IAlertDialogService>(new AlertDialogService(
            (title, message, cancel) => Application.Current!.MainPage!.DisplayAlert(title, message, cancel),
            async (title, message, accept, cancel) => await Application.Current!.MainPage!.DisplayAlert(title, message, accept, cancel)));

        builder.Services.AddSingleton<IPreferencesProvider, PreferencesProvider>();

        return builder.Build();
    }
}
