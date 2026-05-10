// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Components.Modals.Alerts;
using EventLogExpert.Eventing.Common.Databases;
using EventLogExpert.Eventing.Logging;
using EventLogExpert.Eventing.Resolvers;
using EventLogExpert.Services;
using EventLogExpert.UI.Common.Clipboard;
using EventLogExpert.UI.Common.Files;
using EventLogExpert.UI.Common.Preferences;
using EventLogExpert.UI.Common.Versioning;
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

        // Effects implements ILogReloadCoordinator. Fluxor registers Effects as singletons
        // by assembly scan; resolve the same instance through the coordinator interface so callers
        // (SettingsModal) get the single per-app instance with its dictionaries of in-flight loads
        // and close completions.
        builder.Services.AddSingleton<ILogReloadCoordinator>(sp =>
            sp.GetRequiredService<Effects>());

        // Core Services
        builder.Services.AddSingleton<DebugLogService>();
        builder.Services.AddSingleton<ITraceLogger>(sp => sp.GetRequiredService<DebugLogService>());
        builder.Services.AddSingleton<IFileLogger>(sp => sp.GetRequiredService<DebugLogService>());
        builder.Services.AddSingleton<ILogWatcherService, LogWatcherService>();

        var fileLocationOptions = new FileLocationOptions(FileSystem.AppDataDirectory);
        builder.Services.AddSingleton(fileLocationOptions);
        Directory.CreateDirectory(fileLocationOptions.DatabasePath);

        builder.Services.AddSingleton(_ =>
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
        builder.Services.AddSingleton<IApplicationRestartService, WindowsApplicationRestartService>();
        builder.Services.AddSingleton<IPackageDeploymentService, PackageDeploymentService>();
        builder.Services.AddSingleton<IDeploymentService, DeploymentService>();
        builder.Services.AddSingleton<ISettingsService, SettingsService>();
        builder.Services.AddSingleton<DatabaseService>();
        builder.Services.AddSingleton<IDatabaseService>(static provider =>
            provider.GetRequiredService<DatabaseService>());

        // Provider Services
        builder.Services.AddSingleton<IActiveDatabasePathsProvider>(static provider =>
            provider.GetRequiredService<DatabaseService>());
        builder.Services.AddSingleton<IEventResolverCache, EventResolverCache>();
        builder.Services.AddSingleton<IEventXmlResolver, EventXmlResolver>();
        builder.Services.AddTransient<IEventResolver, EventResolver>();

        // UI Services
        builder.Services.AddSingleton<IMainThreadService, MauiMainThreadService>();
        builder.Services.AddSingleton<ITitleProvider, TitleProvider>();
        builder.Services.AddSingleton<IAppTitleService, AppTitleService>();
        builder.Services.AddSingleton<IPreferencesProvider, PreferencesProvider>();
        builder.Services.AddSingleton<IClipboardService, ClipboardService>();
        builder.Services.AddSingleton<IFileSaveService, MauiFileSaveService>();
        builder.Services.AddSingleton<IFilePickerService, MauiFilePickerService>();
        builder.Services.AddSingleton<IFilterService, FilterService>();
        builder.Services.AddSingleton<IPackageVersionProvider, PackageVersionProvider>();
        builder.Services.AddSingleton<IWindowsIdentityProvider, WindowsIdentityProvider>();
        builder.Services.AddSingleton<IModalService, ModalService>();
        builder.Services.AddSingleton<IInlineAlertHostBroker, InlineAlertHostBroker>();
        builder.Services.AddSingleton<IMenuService, MenuService>();
        builder.Services.AddSingleton<MauiMenuActionService>();
        builder.Services.AddSingleton<IMenuActionService>(static provider =>
            provider.GetRequiredService<MauiMenuActionService>());
        builder.Services.AddSingleton<KeyboardShortcutService>();

        builder.Services.AddSingleton<IBannerService, BannerService>();

        builder.Services.AddSingleton<IAlertDialogService>(static provider =>
        {
            var modalService = provider.GetRequiredService<IModalService>();
            var inlineAlertHostBroker = provider.GetRequiredService<IInlineAlertHostBroker>();
            var mainThreadService = provider.GetRequiredService<IMainThreadService>();
            var bannerService = provider.GetRequiredService<IBannerService>();

            return new ModalAlertDialogService(
                inlineAlertHostBroker,
                mainThreadService,
                bannerService,
                parameters => modalService.Show<AlertModal, bool>(parameters.ToDictionary(static kvp => kvp.Key, static kvp => kvp.Value)),
                async parameters =>
                {
                    string? result = await modalService.Show<PromptModal, string>(parameters.ToDictionary(static kvp => kvp.Key, static kvp => kvp.Value));
                    return result ?? string.Empty;
                });
        });

        var mauiApp = builder.Build();

        mauiApp.Services.GetRequiredService<IBannerService>();

        return mauiApp;
    }
}
