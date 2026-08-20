// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.DatabaseTools.DependencyInjection;
using EventLogExpert.Eventing.Readers;
using EventLogExpert.Eventing.Resolvers;
using EventLogExpert.Eventing.Writers;
using EventLogExpert.Filtering.Evaluation;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Logging.Configuration;
using EventLogExpert.Logging.Routing;
using EventLogExpert.Logging.Sinks;
using EventLogExpert.Provider.Maintenance;
using EventLogExpert.Provider.Resolution;
using EventLogExpert.Runtime.ActivityCorrelation;
using EventLogExpert.Runtime.Announcement;
using EventLogExpert.Runtime.Banner;
using EventLogExpert.Runtime.Common.AppTitle;
using EventLogExpert.Runtime.Common.Clipboard;
using EventLogExpert.Runtime.Common.Files;
using EventLogExpert.Runtime.Common.Versioning;
using EventLogExpert.Runtime.Database;
using EventLogExpert.Runtime.DatabaseTools;
using EventLogExpert.Runtime.DatabaseTools.Elevation;
using EventLogExpert.Runtime.DebugLog;
using EventLogExpert.Runtime.EventLog;
using EventLogExpert.Runtime.Export;
using EventLogExpert.Runtime.FilterLenses;
using EventLogExpert.Runtime.FilterLibrary;
using EventLogExpert.Runtime.FilterPane;
using EventLogExpert.Runtime.Histogram;
using EventLogExpert.Runtime.LogTable;
using EventLogExpert.Runtime.LogTable.OrderedView;
using EventLogExpert.Runtime.Scenarios;
using EventLogExpert.Runtime.Scenarios.Favorites;
using EventLogExpert.Runtime.Settings;
using EventLogExpert.Runtime.StatusBar;
using EventLogExpert.Runtime.ResolutionCoverage;
using EventLogExpert.Runtime.Stats;
using EventLogExpert.Runtime.Update;
using EventLogExpert.Runtime.Update.Deployment;
using EventLogExpert.Scenarios.Catalog;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

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
                CategoryLogger(sp, LogCategories.Database));

            store.Refresh();

            return store;
        });

        services.AddSingleton<DatabaseClassificationService>(static sp =>
            ActivatorUtilities.CreateInstance<DatabaseClassificationService>(sp, CategoryLogger(sp, LogCategories.Database)));

        services.AddSingleton<DatabaseUpgradeService>(static sp => new DatabaseUpgradeService(
            sp.GetRequiredService<DatabaseRegistry>(),
            sp.GetRequiredService<DatabaseClassificationService>().InitialClassificationTask,
            sp.GetRequiredService<IProviderDatabaseMaintenance>(),
            CategoryLogger(sp, LogCategories.Database)));

        services.AddSingleton<DatabaseImportService>(static sp =>
            ActivatorUtilities.CreateInstance<DatabaseImportService>(sp, CategoryLogger(sp, LogCategories.Database)));

        services.AddSingleton<DatabaseRecoveryService>(static sp =>
            ActivatorUtilities.CreateInstance<DatabaseRecoveryService>(sp, CategoryLogger(sp, LogCategories.Database)));

        services.AddSingleton<DatabaseService>();
        services.Forward<IDatabaseService, DatabaseService>();
        services.Forward<IActiveDatabases, DatabaseService>();

        services.AddSingleton<IDatabaseOperationCoordinator>(static sp =>
            ActivatorUtilities.CreateInstance<DatabaseOperationCoordinator>(sp, CategoryLogger(sp, LogCategories.Database)));
    }

    private static void AddExportServices(IServiceCollection services)
    {
        services.AddSingleton<ITabularExportWriter, TabularExportWriter>();
        services.AddSingleton<IEventTableExporter, EventTableExporter>();
    }

    private static ITraceLogger CategoryLogger(IServiceProvider serviceProvider, string category) =>
        serviceProvider.GetRequiredService<ILogSourceFactory>().ForCategory(category);

    extension(IServiceCollection services)
    {
        public IServiceCollection AddDatabaseToolsRuntime()
        {
            ArgumentNullException.ThrowIfNull(services);

            services.AddDatabaseToolsServices();
            services.TryAddSingleton<IDatabaseToolsService, DatabaseToolsService>();

            return services;
        }

        public IServiceCollection AddElevatedDatabaseToolsRunner()
        {
            ArgumentNullException.ThrowIfNull(services);

            services.AddSingleton<IElevatedDatabaseToolsRunner>(static sp =>
                new ElevatedDatabaseToolsRunner(
                    sp.GetRequiredService<IElevatedHelperProcessHost>(),
                    sp.GetRequiredService<ILogSourceFactory>().ForCategory(LogCategories.ElevationIpc)));

            return services;
        }

        public IServiceCollection AddEventLogRuntime()
        {
            ArgumentNullException.ThrowIfNull(services);

            AddDatabaseServices(services);
            AddExportServices(services);

            // Commands
            services.AddSingleton<IEventLogCommands, EventLogCommands>();
            services.AddSingleton<IFilterLensCommands, FilterLensCommands>();
            services.AddSingleton<IFilterLibraryCommands, FilterLibraryCommands>();
            services.AddSingleton<IFilterPaneCommands, FilterPaneCommands>();
            services.AddSingleton<IHistogramCommands, HistogramCommands>();
            services.AddSingleton<IStatsCommands, StatsCommands>();
            services.AddSingleton<IStatsService, StatsService>();
            services.AddSingleton<IResolutionCoverageService, ResolutionCoverageService>();
            services.AddSingleton<ILogTableCommands, LogTableCommands>();
            services.AddSingleton<IScenarioFavoriteCommands, ScenarioFavoriteCommands>();

            // Queries
            services.AddSingleton<IEventLogQueries, EventLogQueries>();
            services.AddSingleton<ILogTableQueries, LogTableQueries>();
            services.AddSingleton<IFilterPaneQueries, FilterPaneQueries>();

            // Coordinators and concurrency
            services.AddSingleton<LogCloseCoordinator>();
            services.AddSingleton<EventLogConcurrencyState>();
            services.AddSingleton<XmlFilterMatchCache>();
            services.AddSingleton<IXmlFilterMatcher, XmlFilterMatcher>();
            services.AddSingleton<XmlReloadCoordinator>();
            services.AddSingleton<LiveTailIngestCoordinator>();
            services.AddSingleton<FilteredLogPresenceCoordinator>();
            services.AddSingleton<IEventLogReaderFactory, EventLogReaderFactory>();

            // Ordered-view engine
            services.AddSingleton<OrderedViewWriter>(static _ => new OrderedViewWriter());
            services.AddSingleton<ViewRequestIssuer>();
            services.AddSingleton<OrderedViewDispatchBridge>();
            services.AddSingleton<IOrderedViewSource, OrderedViewSource>();

            // Read-model sources
            services.AddSingleton<IFilterLensSource, FilterLensSource>();
            services.AddSingleton<IOpenLogsPresenceSource, OpenLogsPresenceSource>();
            services.AddSingleton<IHistogramVisibilitySource, HistogramVisibilitySource>();
            services.AddSingleton<IStatsVisibilitySource, StatsVisibilitySource>();
            services.AddSingleton<IFilterAppliedSource, FilterAppliedSource>();
            services.AddSingleton<IEventFocusSource, EventFocusSource>();
            services.AddSingleton<IActiveEventLogSource, ActiveEventLogSource>();
            services.AddSingleton<IEventSelectionSource, EventSelectionSource>();
            services.AddSingleton<IRevealFocusSource, RevealFocusSource>();
            services.AddSingleton<IHistogramDimensionRequestSource, HistogramDimensionRequestSource>();
            services.AddSingleton<IActiveFiltersSource, ActiveFiltersSource>();
            services.AddSingleton<IFilteredDateRangeSource, FilteredDateRangeSource>();
            services.AddSingleton<ILoadedLogNamesSource, LoadedLogNamesSource>();
            services.AddSingleton<ILibraryEntriesSource, LibraryEntriesSource>();
            services.AddSingleton<ILibraryLoadStatusSource, LibraryLoadStatusSource>();
            services.AddSingleton<IScenarioFavoritesSource, ScenarioFavoritesSource>();
            services.AddSingleton<ILogTabBarSource, LogTabBarSource>();
            services.AddSingleton<IStatusBarSource, StatusBarSource>();
            services.AddSingleton<IActivityCorrelationSource, ActivityCorrelationSource>();

            // Change notifiers (concrete raises; interface subscribes; one shared instance)
            services.AddSingleton<GroupCollapseNotifier>();
            services.Forward<IGroupCollapseNotifier, GroupCollapseNotifier>();
            services.AddSingleton<TagBulkUpdateFailedNotifier>();
            services.Forward<ITagBulkUpdateFailedNotifier, TagBulkUpdateFailedNotifier>();
            services.AddSingleton<ClearAllFiltersNotifier>();
            services.Forward<IClearAllFiltersNotifier, ClearAllFiltersNotifier>();
            services.AddSingleton<SetFilterDateRangeSucceededNotifier>();
            services.Forward<ISetFilterDateRangeSucceededNotifier, SetFilterDateRangeSucceededNotifier>();
            services.AddSingleton<FilterPromotedNotifier>();
            services.Forward<IFilterPromotedNotifier, FilterPromotedNotifier>();

            // Indicators, resolvers, formatters, and selectors
            services.AddSingleton<DisplayIndicatorGate>();
            services.AddSingleton<IEventDetailResolver, EventDetailResolver>();
            services.AddSingleton<IActivityCorrelationService, ActivityCorrelationService>();
            services.AddSingleton<IEventXmlResolver, EventXmlResolver>();
            services.AddSingleton<IEventCopyFormatter, EventCopyFormatter>();
            services.AddSingleton<IHighlightSelector, HighlightSelector>();
            services.AddSingleton<ILogTableColumnDefaultsProvider, ColumnDefaults>();
            services.AddSingleton<ILogReloadCoordinator, DatabaseCoordinationEffects>();

            // Application shell: title, banners, and announcements
            services.AddSingleton<IAppTitleService, AppTitleService>();
            services.AddSingleton<BannerService>();
            services.Forward<IAttentionBannerService, BannerService>();
            services.Forward<IProgressBannerService, BannerService>();
            services.Forward<ICriticalErrorService, BannerService>();
            services.Forward<IErrorBannerService, BannerService>();
            services.Forward<IInfoBannerService, BannerService>();
            services.AddSingleton<IExportProgressBannerService, ExportProgressBannerService>();
            services.AddSingleton<IAnnouncementService, AnnouncementService>();

            // Logging
            services.Configure<LoggingOptions>(LoggingOptions.ApplyShippedDefaults);
            services.AddSingleton(static sp =>
            {
                ISettingsService settings = sp.GetRequiredService<ISettingsService>();
                LogRoutingPolicy policy = new(
                    sp.GetRequiredService<IOptions<LoggingOptions>>().Value,
                    settings.LogLevel);

                if (settings.VerboseResolution)
                {
                    policy.SetCategoryOverride(LogCategories.Resolution, LogLevel.Trace);
                }

                return policy;
            });
            services.AddSingleton(static sp => new FileLogSink(
                sp.GetRequiredService<FileLocationOptions>().LoggingPath,
                sp.GetRequiredService<LogRoutingPolicy>(),
                DebugLogFormatter.Format));
            services.AddSingleton<IDebugLogReader>(static sp => new DebugLogFileReader(
                sp.GetRequiredService<FileLocationOptions>(),
                sp.GetRequiredService<FileLogSink>()));
            services.AddSingleton<DebugLogHost>();
            services.AddSingleton<ILogSourceFactory>(static sp =>
            {
                List<ILogSink> sinks = [sp.GetRequiredService<FileLogSink>()];
#if DEBUG
                sinks.Add(new ConsoleSink());
#endif
                return new LogSourceFactory(sinks);
            });
            services.AddSingleton<ITraceLogger>(static sp =>
                sp.GetRequiredService<ILogSourceFactory>().ForCategory(LogSourceFactory.DefaultCategory));
            services.AddKeyedSingleton<ITraceLogger>(LogCategories.EventLog, static (sp, _) =>
                sp.GetRequiredService<ILogSourceFactory>().ForCategory(LogCategories.EventLog));
            services.AddSingleton<IOperationLogProgressFactory, OperationLogProgressFactory>();
            services.AddSingleton<ILogWatcherService, LogWatcherService>();
            services.AddSingleton<ISettingsService, SettingsService>();

            // Update and deployment
            services.AddSingleton<ICurrentVersionProvider, CurrentVersionProvider>();
            services.AddSingleton<IDeploymentService, DeploymentService>();
            services.AddSingleton<IGitHubService, GitHubService>();
            services.AddSingleton<IPackageDeploymentService, PackageDeploymentService>();
            services.AddSingleton<IPackageVersionProvider, PackageVersionProvider>();
            services.AddSingleton<IUpdateService, UpdateService>();

            // Database tools
            services.AddDatabaseToolsServices();
            services.TryAddSingleton<IDatabaseToolsService, DatabaseToolsService>();

            // Scenarios and channels
            services.AddSingleton<IScenarioSource, BuiltInScenarioSource>();
            services.AddSingleton<BuiltInScenarioRegistry>();
            services.AddSingleton<IChannelConfigReader>(static sp =>
                new EventLogChannelConfigReader(CategoryLogger(sp, LogCategories.EventLog)));
            services.AddSingleton<IChannelConfigWriter>(static sp =>
                new ChannelConfigWriter(CategoryLogger(sp, LogCategories.EventLog)));
            services.AddSingleton<ChannelPresenceProbe>();
            services.Forward<IChannelPresenceProbe, ChannelPresenceProbe>();
            services.Forward<IChannelReadinessService, ChannelPresenceProbe>();
            services.AddSingleton<IChannelEnableService, ChannelEnableService>();
            services.AddSingleton<IEvtxChannelReader, EvtxChannelReader>();
            services.AddSingleton<IScenarioQueryService, ScenarioQueryService>();
            services.AddSingleton<IScenarioLaunchService, ScenarioLaunchService>();
            services.AddSingleton<IScenarioApplyService, ScenarioApplyService>();
            services.AddSingleton<IScenarioAuthoringService, ScenarioAuthoringService>();

            return services;
        }
    }
}
