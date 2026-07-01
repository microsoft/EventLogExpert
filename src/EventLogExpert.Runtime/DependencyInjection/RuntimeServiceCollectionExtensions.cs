// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.DatabaseTools.DependencyInjection;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Logging.Routing;
using EventLogExpert.Logging.Sinks;
using EventLogExpert.Provider.Maintenance;
using EventLogExpert.Provider.Resolution;
using EventLogExpert.Runtime.Announcement;
using EventLogExpert.Runtime.Banner;
using EventLogExpert.Runtime.Common.AppTitle;
using EventLogExpert.Runtime.Common.Files;
using EventLogExpert.Runtime.Common.Versioning;
using EventLogExpert.Runtime.Database;
using EventLogExpert.Runtime.DatabaseTools;
using EventLogExpert.Runtime.DatabaseTools.Elevation;
using EventLogExpert.Runtime.DebugLog;
using EventLogExpert.Runtime.EventLog;
using EventLogExpert.Runtime.Export;
using EventLogExpert.Runtime.FilterLibrary;
using EventLogExpert.Runtime.FilterPane;
using EventLogExpert.Runtime.LogTable;
using EventLogExpert.Runtime.Menu;
using EventLogExpert.Runtime.Modal;
using EventLogExpert.Runtime.Scenarios;
using EventLogExpert.Runtime.Scenarios.Favorites;
using EventLogExpert.Runtime.Settings;
using EventLogExpert.Runtime.Update;
using EventLogExpert.Runtime.Update.Deployment;
using EventLogExpert.Scenarios.Catalog;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Extensions.DependencyInjection;

public static class RuntimeServiceCollectionExtensions
{
    private static void AddDatabaseServices(IServiceCollection services)
    {
        services.AddSingleton<DatabaseRegistry>(static sp =>
        {
            var store = new DatabaseRegistry(
                sp.GetRequiredService<FileLocationOptions>(),
                sp.GetRequiredService<IDatabasePreferencesProvider>(),
                sp.GetRequiredService<ITraceLogger>());

            store.Refresh();

            return store;
        });

        services.AddSingleton<DatabaseClassificationService>();

        services.AddSingleton<DatabaseUpgradeService>(static sp => new DatabaseUpgradeService(
            sp.GetRequiredService<DatabaseRegistry>(),
            sp.GetRequiredService<DatabaseClassificationService>().InitialClassificationTask,
            sp.GetRequiredService<IProviderDatabaseMaintenance>(),
            sp.GetRequiredService<ITraceLogger>()));

        services.AddSingleton<DatabaseImportService>();
        services.AddSingleton<DatabaseRecoveryService>();

        services.AddSingleton<DatabaseService>();
        services.AddSingleton<IDatabaseService>(static sp => sp.GetRequiredService<DatabaseService>());
        services.AddSingleton<IActiveDatabases>(static sp => sp.GetRequiredService<DatabaseService>());

        services.AddSingleton<IDatabaseOperationCoordinator, DatabaseOperationCoordinator>();
    }

    private static void AddExportServices(IServiceCollection services)
    {
        services.AddSingleton<ITabularExportWriter, TabularExportWriter>();
        services.AddSingleton<IEventTableExporter, EventTableExporter>();
    }

    extension(IServiceCollection services)
    {
        /// <summary>
        ///     Helper-friendly subset of <see cref="AddEventLogRuntime" />. Registers ONLY
        ///     <see cref="IDatabaseToolsService" /> and its operation factory dependency. Used by the packaged elevation helper
        ///     which needs to run DatabaseTools operations but must NOT pull in the rest of the runtime (Fluxor, banner services,
        ///     settings, etc.) - those would require host services (file pickers, modal coordinators) that don't exist in a
        ///     console helper.
        /// </summary>
        public IServiceCollection AddDatabaseToolsRuntime()
        {
            ArgumentNullException.ThrowIfNull(services);

            services.AddDatabaseToolsServices();
            services.TryAddSingleton<IDatabaseToolsService, DatabaseToolsService>();

            return services;
        }

        /// <summary>
        ///     Registers <see cref="IElevatedDatabaseToolsRunner" /> backed by the in-Runtime
        ///     <see cref="ElevatedDatabaseToolsRunner" /> implementation. Callers MUST also register
        ///     <see cref="IElevatedHelperProcessHost" /> separately (the production implementation lives in the MAUI head's
        ///     adapter layer; tests substitute scripted fakes).
        /// </summary>
        public IServiceCollection AddElevatedDatabaseToolsRunner()
        {
            ArgumentNullException.ThrowIfNull(services);

            services.AddSingleton<IElevatedDatabaseToolsRunner, ElevatedDatabaseToolsRunner>();

            return services;
        }

        /// <summary>
        ///     Registers the runtime tier's services. Callers MUST also register:
        ///     <list type="bullet">
        ///         <item>
        ///             <c>AddFluxor(...)</c> - effect classes and state selectors depend on <c>IDispatcher</c>,
        ///             <c>IState&lt;T&gt;</c>, etc.
        ///         </item>
        ///         <item><c>AddEventLogFiltering()</c> - effect classes depend on <c>IFilterService</c>.</item>
        ///         <item>
        ///             <c>AddEventLogProviderDatabase()</c> - database sub-services depend on
        ///             <c>IProviderDatabaseMaintenance</c>.
        ///         </item>
        ///         <item>
        ///             <c>IFilePickerService</c> - <c>DatabaseOperationCoordinator</c> depends on it for Import. Host registers
        ///             a concrete implementation (e.g., <c>MauiFilePickerService</c>).
        ///         </item>
        ///         <item>
        ///             <c>IFilterLibraryStore</c> - <c>FilterLibrary</c> effects depend on it. Register the default
        ///             SQLite-backed store via <c>services.AddFilterLibrarySqliteStore(<i>dbPath</i>)</c>, or supply a custom
        ///             implementation.
        ///         </item>
        ///         <item>
        ///             <c>IScenarioFavoriteStore</c> - the scenario favorites effects depend on it. Register the default
        ///             SQLite-backed store via <c>services.AddScenarioFavoriteSqliteStore(<i>dbPath</i>)</c>, or supply a custom
        ///             implementation.
        ///         </item>
        ///         <item>
        ///             <c>IMenuActionService</c> - the scenario launch service depends on it to open a scenario's channels. The
        ///             host registers the concrete implementation (e.g., <c>MauiMenuActionService</c>).
        ///         </item>
        ///     </list>
        ///     Omitting any of these produces a DI resolution failure when the dependent services are first activated.
        /// </summary>
        public IServiceCollection AddEventLogRuntime()
        {
            ArgumentNullException.ThrowIfNull(services);

            AddDatabaseServices(services);
            AddExportServices(services);

            // Command facades.
            services.AddSingleton<IEventLogCommands, EventLogCommands>();
            services.AddSingleton<IFilterLibraryCommands, FilterLibraryCommands>();
            services.AddSingleton<IFilterPaneCommands, FilterPaneCommands>();
            services.AddSingleton<ILogTableCommands, LogTableCommands>();
            services.AddSingleton<IScenarioFavoriteCommands, ScenarioFavoriteCommands>();

            // Query facades.
            services.AddSingleton<IEventLogQueries, EventLogQueries>();

            // Shared coordination state for EventLog effects classes.
            services.AddSingleton<LogCloseCoordinator>();
            services.AddSingleton<EventLogConcurrencyState>();
            services.AddSingleton<PartialLoadCoordinator>();
            services.AddSingleton<IEventLogReaderFactory, EventLogReaderFactory>();

            // UI capabilities.
            services.AddSingleton<IHighlightSelector, HighlightSelector>();
            services.AddSingleton<ILogTableColumnDefaultsProvider, ColumnDefaults>();
            services.AddSingleton<DatabaseCoordinationEffects>();
            services.AddSingleton<ILogReloadCoordinator>(static sp => sp.GetRequiredService<DatabaseCoordinationEffects>());

            // Application services.
            services.AddSingleton<IAppTitleService, AppTitleService>();

            // BannerService is the shared backing store for the 5 banner facets.
            // Registered once as the concrete type, then each facet interface resolves
            // back to the same singleton instance. This preserves cross-facet state
            // invariants (single lock, single backing store) while letting consumers
            // depend only on the narrow interface they need.
            services.AddSingleton<BannerService>();
            services.AddSingleton<IAttentionBannerService>(static sp => sp.GetRequiredService<BannerService>());
            services.AddSingleton<IProgressBannerService>(static sp => sp.GetRequiredService<BannerService>());
            services.AddSingleton<ICriticalErrorService>(static sp => sp.GetRequiredService<BannerService>());
            services.AddSingleton<IErrorBannerService>(static sp => sp.GetRequiredService<BannerService>());
            services.AddSingleton<IInfoBannerService>(static sp => sp.GetRequiredService<BannerService>());

            // Export progress is a standalone banner facet with no database coupling, so it is a
            // separate singleton from BannerService. The UI BannerCycleStateService consumes it as a
            // 7th source; the head menu driver brackets a streaming export with Begin/End.
            services.AddSingleton<IExportProgressBannerService, ExportProgressBannerService>();

            services.AddSingleton<IAnnouncementService, AnnouncementService>();

            services.AddSingleton<DebugFileSink>();
            services.AddSingleton<IFileLogger>(static sp => sp.GetRequiredService<DebugFileSink>());
            services.AddSingleton<ILogSourceFactory>(static sp =>
            {
                List<ILogSink> sinks = [sp.GetRequiredService<DebugFileSink>()];
#if DEBUG
                // Debug builds also mirror to the console so logs surface in the F12 / debug window; Release is file-only.
                sinks.Add(new ConsoleSink());
#endif
                return new LogSourceFactory(sinks);
            });
            services.AddSingleton<ITraceLogger>(static sp =>
                sp.GetRequiredService<ILogSourceFactory>().ForCategory(LogSourceFactory.DefaultCategory));
            services.AddSingleton<ILogWatcherService, LogWatcherService>();
            services.AddSingleton<IMenuService, MenuService>();
            services.AddSingleton<IModalCoordinator, ModalCoordinator>();
            services.AddSingleton<IModalService, ModalService>();
            services.AddSingleton<ISettingsService, SettingsService>();

            // Update + deployment services.
            services.AddSingleton<ICurrentVersionProvider, CurrentVersionProvider>();
            services.AddSingleton<IDeploymentService, DeploymentService>();
            services.AddSingleton<IGitHubService, GitHubService>();
            services.AddSingleton<IPackageDeploymentService, PackageDeploymentService>();
            services.AddSingleton<IPackageVersionProvider, PackageVersionProvider>();
            services.AddSingleton<IUpdateService, UpdateService>();

            // DatabaseTools service (CLI-equivalent operations exposed to the UI).
            services.AddDatabaseToolsServices();
            services.TryAddSingleton<IDatabaseToolsService, DatabaseToolsService>();

            // Built-in scenarios: the immutable embedded catalog + presence/query/launch services. The registry
            // aggregates every registered IScenarioSource (PR1 ships only the built-in source).
            services.AddSingleton<IScenarioSource, BuiltInScenarioSource>();
            services.AddSingleton<BuiltInScenarioRegistry>();
            services.AddSingleton<IChannelPresenceProbe, ChannelPresenceProbe>();
            services.AddSingleton<IScenarioQueryService, ScenarioQueryService>();
            services.AddSingleton<IScenarioLaunchService, ScenarioLaunchService>();
            services.AddSingleton<IScenarioApplyService, ScenarioApplyService>();
            services.AddSingleton<IScenarioAuthoringService, ScenarioAuthoringService>();

            return services;
        }
    }
}
