// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.EventResolvers;
using EventLogExpert.Eventing.Helpers;
using EventLogExpert.Services;
using EventLogExpert.UI.Interfaces;
using EventLogExpert.UI.Options;
using EventLogExpert.UI.Services;
using EventLogExpert.UI.Store;
using EventLogExpert.UI.Store.EventLog;
using Fluxor;

namespace EventLogExpert;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();

        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
            });

        builder.Services.AddMauiBlazorWebView();
        builder.Services.AddBlazorWebViewDeveloperTools();

        builder.Services.AddFluxor(options =>
        {
            options.ScanAssemblies(typeof(MauiProgram).Assembly).WithLifetime(StoreLifetime.Singleton);
            options.AddMiddleware<LoggingMiddleware>();
        });

        // Core Services
        builder.Services.AddSingleton<ITraceLogger, DebugLogService>();
        builder.Services.AddSingleton<ILogWatcherService, LiveLogWatcherService>();

        var fileLocationOptions = new FileLocationOptions(FileSystem.AppDataDirectory);
        builder.Services.AddSingleton(fileLocationOptions);
        Directory.CreateDirectory(fileLocationOptions.DatabasePath);

        // Build Services
        builder.Services.AddSingleton<ICurrentVersionProvider, CurrentVersionProvider>();
        builder.Services.AddSingleton<IUpdateService, UpdateService>();
        builder.Services.AddSingleton<IGitHubService, GitHubService>();
        builder.Services.AddSingleton<IDeploymentService, DeploymentService>();

        // Provider Services
        builder.Services.AddSingleton<IEnabledDatabaseCollectionProvider, EnabledDatabaseCollectionProvider>();
        builder.Services.AddSingleton<IDatabaseCollectionProvider, EnabledDatabaseCollectionProvider>();
        builder.Services.AddSingleton<IEventResolverCache, EventResolverCache>();
        builder.Services.AddTransient<IEventResolver, VersatileEventResolver>();

        // UI Services
        builder.Services.AddSingleton<IMainThreadService>(new MainThreadService(MainThread.InvokeOnMainThreadAsync));
        builder.Services.AddSingleton<ITitleProvider, TitleProvider>();
        builder.Services.AddSingleton<IAppTitleService, AppTitleService>();
        builder.Services.AddSingleton<IPreferencesProvider, PreferencesProvider>();
        builder.Services.AddSingleton<IClipboardService, ClipboardService>();

        builder.Services.AddSingleton<IAlertDialogService>(new AlertDialogService(
            async (title, message, cancel) => await Application.Current!.MainPage!.DisplayAlert(title, message, cancel),
            async (title, message, accept, cancel) => await Application.Current!.MainPage!.DisplayAlert(title, message, accept, cancel),
            async (title, message) => await Application.Current!.MainPage!.DisplayPromptAsync(title, message),
            async (title, message, value) => await Application.Current!.MainPage!.DisplayPromptAsync(title, message, initialValue: value)));

        return builder.Build();
    }
}
