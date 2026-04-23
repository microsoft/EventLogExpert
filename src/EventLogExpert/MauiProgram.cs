// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.EventResolvers;
using EventLogExpert.Eventing.Helpers;
using EventLogExpert.Services;
using EventLogExpert.Shared.Components.Alerts;
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

#if RELEASE
        if (Environment.GetCommandLineArgs().Contains("/EnableConsole", StringComparer.OrdinalIgnoreCase))
#endif
        {
            builder.Services.AddBlazorWebViewDeveloperTools();
        }

        builder.Services.AddFluxor(options =>
        {
            options.ScanAssemblies(typeof(MauiProgram).Assembly).WithLifetime(StoreLifetime.Singleton);
            options.AddMiddleware<LoggingMiddleware>();
        });

        // Core Services
        builder.Services.AddSingleton<DebugLogService>();
        builder.Services.AddSingleton<ITraceLogger>(sp => sp.GetRequiredService<DebugLogService>());
        builder.Services.AddSingleton<IFileLogger>(sp => sp.GetRequiredService<DebugLogService>());
        builder.Services.AddSingleton<ILogWatcherService, LiveLogWatcherService>();

        var fileLocationOptions = new FileLocationOptions(FileSystem.AppDataDirectory);
        builder.Services.AddSingleton(fileLocationOptions);
        Directory.CreateDirectory(fileLocationOptions.DatabasePath);

        builder.Services.AddSingleton<HttpClient>(_ =>
        {
            HttpClient client = new() { BaseAddress = new Uri("https://api.github.com/") };

            client.DefaultRequestHeaders.Add("Accept", "application/vnd.github+json");
            client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
            client.DefaultRequestHeaders.UserAgent.TryParseAdd("request");

            return client;
        });

        // Build Services
        builder.Services.AddSingleton<ICurrentVersionProvider, CurrentVersionProvider>();
        builder.Services.AddSingleton<IUpdateService, UpdateService>();
        builder.Services.AddSingleton<IGitHubService, GitHubService>();
        builder.Services.AddSingleton<IApplicationRestartService, ApplicationRestartService>();
        builder.Services.AddSingleton<IPackageDeploymentService, PackageDeploymentService>();
        builder.Services.AddSingleton<IDeploymentService, DeploymentService>();
        builder.Services.AddSingleton<ISettingsService, SettingsService>();
        builder.Services.AddSingleton<IDatabaseService, DatabaseService>();

        // Provider Services
        builder.Services.AddSingleton<IEnabledDatabaseCollectionProvider, EnabledDatabaseCollectionProvider>();
        builder.Services.AddSingleton<IDatabaseCollectionProvider, EnabledDatabaseCollectionProvider>();
        builder.Services.AddSingleton<IEventResolverCache, EventResolverCache>();
        builder.Services.AddSingleton<IEventXmlResolver, EventXmlResolver>();
        builder.Services.AddTransient<IEventResolver, EventResolver>();

        // UI Services
        builder.Services.AddSingleton<IMainThreadService>(new MainThreadService(
            MainThread.InvokeOnMainThreadAsync,
            MainThread.InvokeOnMainThreadAsync));
        builder.Services.AddSingleton<ITitleProvider, TitleProvider>();
        builder.Services.AddSingleton<IAppTitleService, AppTitleService>();
        builder.Services.AddSingleton<IPreferencesProvider, PreferencesProvider>();
        builder.Services.AddSingleton<IClipboardService, ClipboardService>();
        builder.Services.AddSingleton<IFilterService, FilterService>();
        builder.Services.AddSingleton<IPackageVersionProvider, PackageVersionProvider>();
        builder.Services.AddSingleton<IWindowsIdentityProvider, WindowsIdentityProvider>();
        builder.Services.AddSingleton<IModalService, ModalService>();

        builder.Services.AddSingleton<IAlertDialogService>(static provider =>
        {
            var modalService = provider.GetRequiredService<IModalService>();
            var mainThreadService = provider.GetRequiredService<IMainThreadService>();

            return new ModalAlertDialogService(
                modalService,
                mainThreadService,
                parameters => modalService.Show<AlertModal, bool>(parameters.ToDictionary(static kvp => kvp.Key, static kvp => kvp.Value)),
                async parameters =>
                {
                    string? result = await modalService.Show<PromptModal, string>(parameters.ToDictionary(static kvp => kvp.Key, static kvp => kvp.Value));
                    return result ?? string.Empty;
                });
        });

        return builder.Build();
    }
}
