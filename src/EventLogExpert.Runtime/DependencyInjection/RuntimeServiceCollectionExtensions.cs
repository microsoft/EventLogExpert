// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Provider.Maintenance;
using EventLogExpert.Provider.Resolution;
using EventLogExpert.Runtime.Announcement;
using EventLogExpert.Runtime.Banner;
using EventLogExpert.Runtime.Common.AppTitle;
using EventLogExpert.Runtime.Common.Files;
using EventLogExpert.Runtime.Common.Versioning;
using EventLogExpert.Runtime.Database;
using EventLogExpert.Runtime.DatabaseTools;
using EventLogExpert.Runtime.DebugLog;
using EventLogExpert.Runtime.EventLog;
using EventLogExpert.Runtime.FilterCache;
using EventLogExpert.Runtime.FilterGroup;
using EventLogExpert.Runtime.FilterLibrary;
using EventLogExpert.Runtime.FilterPane;
using EventLogExpert.Runtime.LogTable;
using EventLogExpert.Runtime.Menu;
using EventLogExpert.Runtime.Modal;
using EventLogExpert.Runtime.Settings;
using EventLogExpert.Runtime.Update;
using EventLogExpert.Runtime.Update.Deployment;

namespace Microsoft.Extensions.DependencyInjection;

public static class RuntimeServiceCollectionExtensions
{
    /// <summary>
    ///     Registers the runtime tier's services. Callers MUST also register:
    ///     <list type="bullet">
    ///         <item>
    ///             <c>AddFluxor(...)</c> — effect classes and state selectors depend on <c>IDispatcher</c>,
    ///             <c>IState&lt;T&gt;</c>, etc.
    ///         </item>
    ///         <item><c>AddEventLogFiltering()</c> — effect classes depend on <c>IFilterService</c>.</item>
    ///         <item>
    ///             <c>AddEventLogProviderDatabase()</c> — database sub-services depend on
    ///             <c>IProviderDatabaseMaintenance</c>.
    ///         </item>
    ///         <item>
    ///             <c>IFilePickerService</c> — <c>DatabaseOperationCoordinator</c> depends on it for Import.
    ///             Host registers a concrete implementation (e.g., <c>MauiFilePickerService</c>).
    ///         </item>
    ///         <item>
    ///             <c>IFilterLibraryStore</c> — <c>FilterLibrary</c> effects depend on it. Register the
    ///             default SQLite-backed store via
    ///             <c>services.AddFilterLibrarySqliteStore(<i>dbPath</i>)</c>, or supply a custom implementation.
    ///         </item>
    ///     </list>
    ///     Omitting any of these produces a DI resolution failure when the dependent services are first activated.
    /// </summary>
    public static IServiceCollection AddEventLogRuntime(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        AddDatabaseServices(services);

        // Command facades.
        services.AddSingleton<IEventLogCommands, EventLogCommands>();
        services.AddSingleton<IFilterCacheCommands, FilterCacheCommands>();
        services.AddSingleton<IFilterGroupCommands, FilterGroupCommands>();
        services.AddSingleton<IFilterLibraryCommands, FilterLibraryCommands>();
        services.AddSingleton<IFilterPaneCommands, FilterPaneCommands>();
        services.AddSingleton<ILogTableCommands, LogTableCommands>();

        // Shared coordination state for EventLog effects classes.
        services.AddSingleton<LogCloseCoordinator>();
        services.AddSingleton<EventLogConcurrencyState>();

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

        services.AddSingleton<IAnnouncementService, AnnouncementService>();

        services.AddSingleton<DebugLogService>();
        services.AddSingleton<ITraceLogger>(static sp => sp.GetRequiredService<DebugLogService>());
        services.AddSingleton<IFileLogger>(static sp => sp.GetRequiredService<DebugLogService>());
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
        services.AddSingleton<IDatabaseToolsOperationFactory, DatabaseToolsOperationFactory>();
        services.AddSingleton<IDatabaseToolsService, DatabaseToolsService>();

        return services;
    }

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
}
