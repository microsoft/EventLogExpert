// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Adapters.Input;
using EventLogExpert.Adapters.Settings;
using EventLogExpert.DependencyInjection;
using EventLogExpert.Eventing.Resolvers;
using EventLogExpert.Runtime.Banner;
using EventLogExpert.Runtime.Common.Files;
using EventLogExpert.Runtime.Database;
using EventLogExpert.Runtime.FilterLibrary;
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

        // FilterLibrary persistence + gated legacy migration (see LegacyMigrationFeature for the removal contract)
        builder.Services.AddFilterLibrarySqliteStore(
            Path.Combine(FileSystem.AppDataDirectory, "filter-library.db"));

        if (LegacyMigrationFeature.Enabled)
        {
            builder.Services.AddSingleton<ILegacyPreferences, MauiLegacyPreferencesAdapter>();
        }

        builder.Services.AddLegacyFilterMigration();

        // Top-level layer registration
        builder.Services.AddEventLogFiltering();
        builder.Services.AddEventLogRuntime();
        builder.Services.AddElevatedDatabaseToolsRunner();
        builder.Services.AddEventLogUiServices();

        // Host-side DI groupings (see MauiProgramExtensions for membership)
        builder.Services.AddMauiPreferenceAdapters();
        builder.Services.AddMauiPlatformAdapters();
        builder.Services.AddMauiMenuServices();
        builder.Services.AddMauiActivationDispatcher();
        builder.Services.AddMauiAlertDialogService();

        builder.Services.AddSingleton<IBannerCycleStateService, BannerCycleStateService>();
        builder.Services.AddSingleton<KeyboardShortcutService>();
        builder.Services.AddSingleton<DatabaseRecoveryHost>();

        var mauiApp = builder.Build();

        // Force eager construction of BannerService — its ctor subscribes to IDatabaseService.EntriesChanged +
        // UpgradeBatch* events. Any facet resolves to the same singleton instance.
        mauiApp.Services.GetRequiredService<IAttentionBannerService>();
        mauiApp.Services.GetRequiredService<DatabaseRecoveryHost>();

        return mauiApp;
    }
}