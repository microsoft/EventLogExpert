// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Adapters.Settings;
using EventLogExpert.DependencyInjection;
using EventLogExpert.Eventing.Resolvers;
using EventLogExpert.Runtime.Banner;
using EventLogExpert.Runtime.Common.Files;
using EventLogExpert.Runtime.FilterLibrary;
using EventLogExpert.Runtime.LogTable;
using EventLogExpert.Runtime.Scenarios;
using EventLogExpert.Runtime.Scenarios.Favorites;
using EventLogExpert.UI.Banner;
using EventLogExpert.UI.Database;
using Fluxor;
using Fluxor.DependencyInjection;

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

#if DEBUG
        const bool ScenarioAuthoringEnabled = true;
#else
        bool ScenarioAuthoringEnabled = Environment.GetCommandLineArgs()
            .Contains("/EnableScenarioAuthoring", StringComparer.OrdinalIgnoreCase);
#endif
        builder.Services.AddSingleton(new ScenarioAuthoringOptions(ScenarioAuthoringEnabled));

        builder.Services.AddFluxor(options =>
        {
            options.ScanAssemblies(typeof(MauiProgram).Assembly)
                .RegisterStateLibrary()
                .WithLifetime(StoreLifetime.Singleton);
        });

        // Bootstrap infrastructure
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

        // Provider event-log resolvers
        builder.Services.AddEventLogProviderDatabase();
        builder.Services.AddSingleton<IEventResolverCache, EventResolverCache>();
        builder.Services.AddSingleton<IEventXmlResolver, EventXmlResolver>();
        builder.Services.AddTransient<IEventResolver, EventResolver>();

        // FilterLibrary persistence
        var appDbPath = Path.Combine(FileSystem.AppDataDirectory, "filter-library.db");
        builder.Services.AddFilterLibrarySqliteStore(appDbPath);
        builder.Services.AddScenarioFavoriteSqliteStore(appDbPath);

        builder.Services.AddFilterLibraryExport();

        if (LegacyMigrationFeature.Enabled || BackslashMigrationFeature.Enabled || ColumnResetMigrationFeature.Enabled)
        {
            builder.Services.AddSingleton<ILegacyPreferences, MauiLegacyPreferencesAdapter>();
        }

        // TODO: Disable this in the next release build
        builder.Services.AddLegacyFilterMigration();
        builder.Services.AddBackslashNameMigration();
        builder.Services.AddColumnResetMigration();

        // Top-level layer registration
        builder.Services.AddEventLogFiltering();
        builder.Services.AddEventLogRuntime();
        builder.Services.AddElevatedDatabaseToolsRunner();
        builder.Services.AddEventLogUiServices();

        // Host-side DI groupings (see MauiProgramExtensions for membership)
        builder.Services.AddMauiPreferenceAdapters();
        builder.Services.AddMauiPlatformAdapters();
        builder.Services.AddWindowsPlatformAdapters();
        builder.Services.AddMauiMenuServices();
        builder.Services.AddMauiActivationDispatcher();
        builder.Services.AddMauiAlertDialogService();

        builder.Services.AddSingleton<IBannerCycleStateService, BannerCycleStateService>();
        builder.Services.AddSingleton<DatabaseRecoveryHost>();

        var mauiApp = builder.Build();

        // Force eager construction of BannerService — its ctor subscribes to IDatabaseService.EntriesChanged +
        // UpgradeBatch* events. Any facet resolves to the same singleton instance.
        mauiApp.Services.GetRequiredService<IAttentionBannerService>();
        mauiApp.Services.GetRequiredService<DatabaseRecoveryHost>();

        return mauiApp;
    }
}
