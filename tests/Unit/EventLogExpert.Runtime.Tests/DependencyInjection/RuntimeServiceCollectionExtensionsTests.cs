// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.DatabaseTools.Common.Operations;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Logging.Routing;
using EventLogExpert.Logging.Sinks;
using EventLogExpert.Provider.Maintenance;
using EventLogExpert.Provider.Resolution;
using EventLogExpert.Runtime.Alerts;
using EventLogExpert.Runtime.Announcement;
using EventLogExpert.Runtime.Banner;
using EventLogExpert.Runtime.Common.AppTitle;
using EventLogExpert.Runtime.Common.Files;
using EventLogExpert.Runtime.Common.Identity;
using EventLogExpert.Runtime.Common.Restart;
using EventLogExpert.Runtime.Common.Threading;
using EventLogExpert.Runtime.Common.Versioning;
using EventLogExpert.Runtime.Database;
using EventLogExpert.Runtime.DatabaseTools;
using EventLogExpert.Runtime.DatabaseTools.Elevation;
using EventLogExpert.Runtime.DebugLog;
using EventLogExpert.Runtime.EventLog;
using EventLogExpert.Runtime.FilterLenses;
using EventLogExpert.Runtime.FilterLibrary;
using EventLogExpert.Runtime.FilterPane;
using EventLogExpert.Runtime.Histogram;
using EventLogExpert.Runtime.LogTable;
using EventLogExpert.Runtime.Menu;
using EventLogExpert.Runtime.Scenarios;
using EventLogExpert.Runtime.Scenarios.Favorites;
using EventLogExpert.Runtime.Settings;
using EventLogExpert.Runtime.StatusBar;
using EventLogExpert.Runtime.Update;
using EventLogExpert.Runtime.Update.Deployment;
using EventLogExpert.Scenarios.Catalog;
using Fluxor;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace EventLogExpert.Runtime.Tests.DependencyInjection;

public sealed class RuntimeServiceCollectionExtensionsTests
{
    [Theory]
    [InlineData(typeof(IDatabaseToolsOperationFactory))]
    [InlineData(typeof(IDatabaseToolsService))]
    public async Task AddDatabaseToolsRuntime_ShouldResolveDatabaseToolsAbstraction(Type serviceType)
    {
        var services = new ServiceCollection();
        services.AddDatabaseToolsRuntime();

        await using var provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });

        await using var scope = provider.CreateAsyncScope();

        var resolved = scope.ServiceProvider.GetService(serviceType);

        Assert.NotNull(resolved);
    }

    [Fact]
    public async Task AddElevatedDatabaseToolsRunner_ShouldResolveRunnerWhenHostIsRegistered()
    {
        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<IElevatedHelperProcessHost>());
        var logSourceFactory = Substitute.For<ILogSourceFactory>();
        logSourceFactory.ForCategory(Arg.Any<string>()).Returns(Substitute.For<ITraceLogger>());
        services.AddSingleton(logSourceFactory);
        services.AddElevatedDatabaseToolsRunner();

        await using var provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });

        await using var scope = provider.CreateAsyncScope();

        var resolved = scope.ServiceProvider.GetService<IElevatedDatabaseToolsRunner>();

        Assert.NotNull(resolved);
    }

    [Theory]
    [InlineData(typeof(IAttentionBannerService))]
    [InlineData(typeof(IProgressBannerService))]
    [InlineData(typeof(ICriticalErrorService))]
    [InlineData(typeof(IErrorBannerService))]
    [InlineData(typeof(IInfoBannerService))]
    public async Task AddEventLogRuntime_BannerFacets_AreSingletons(Type facetType)
    {
        var services = new ServiceCollection();
        RegisterHostDependencies(services);
        services.AddEventLogRuntime();

        await using var provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });

        await using var scope1 = provider.CreateAsyncScope();
        await using var scope2 = provider.CreateAsyncScope();

        var first = scope1.ServiceProvider.GetService(facetType);
        var second = scope2.ServiceProvider.GetService(facetType);

        Assert.NotNull(first);
        Assert.Same(first, second);
    }

    [Fact]
    public async Task AddEventLogRuntime_Resolves5BannerFacetsToSameSingleton()
    {
        var services = new ServiceCollection();
        RegisterHostDependencies(services);
        services.AddEventLogRuntime();

        await using var provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });

        await using var scope1 = provider.CreateAsyncScope();
        await using var scope2 = provider.CreateAsyncScope();

        var attentionScope1 = scope1.ServiceProvider.GetRequiredService<IAttentionBannerService>();
        var progressScope1 = scope1.ServiceProvider.GetRequiredService<IProgressBannerService>();
        var criticalScope1 = scope1.ServiceProvider.GetRequiredService<ICriticalErrorService>();
        var errorScope1 = scope1.ServiceProvider.GetRequiredService<IErrorBannerService>();
        var infoScope1 = scope1.ServiceProvider.GetRequiredService<IInfoBannerService>();
        var attentionScope2 = scope2.ServiceProvider.GetRequiredService<IAttentionBannerService>();

        Assert.Same(attentionScope1, progressScope1);
        Assert.Same(attentionScope1, criticalScope1);
        Assert.Same(attentionScope1, errorScope1);
        Assert.Same(attentionScope1, infoScope1);
        Assert.Same(attentionScope1, attentionScope2);
    }

    [Fact]
    public async Task AddEventLogRuntime_SharesOneFileLogSinkSingleton()
    {
        var services = new ServiceCollection();
        RegisterHostDependencies(services);
        services.AddEventLogRuntime();

        await using var provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });

        await using var scope1 = provider.CreateAsyncScope();
        await using var scope2 = provider.CreateAsyncScope();

        var sinkScope1 = scope1.ServiceProvider.GetRequiredService<FileLogSink>();
        var sinkScope2 = scope2.ServiceProvider.GetRequiredService<FileLogSink>();

        Assert.Same(sinkScope1, sinkScope2);
        Assert.NotNull(scope1.ServiceProvider.GetRequiredService<IDebugLogReader>());
        Assert.NotNull(scope1.ServiceProvider.GetRequiredService<IOperationLogProgressFactory>());
    }

    [Theory]
    [InlineData(typeof(IEventLogCommands))]
    [InlineData(typeof(IFilterLibraryCommands))]
    [InlineData(typeof(IFilterPaneCommands))]
    [InlineData(typeof(ILogTableCommands))]
    [InlineData(typeof(IScenarioFavoriteCommands))]
    [InlineData(typeof(IEventLogQueries))]
    [InlineData(typeof(ILogTableQueries))]
    [InlineData(typeof(IFilterPaneQueries))]
    [InlineData(typeof(IFilterLensSource))]
    [InlineData(typeof(IOpenLogsPresenceSource))]
    [InlineData(typeof(IHistogramVisibilitySource))]
    [InlineData(typeof(IEventFocusSource))]
    [InlineData(typeof(IActiveEventLogSource))]
    [InlineData(typeof(IFilterAppliedSource))]
    [InlineData(typeof(IScenarioFavoritesSource))]
    [InlineData(typeof(ILogTabBarSource))]
    [InlineData(typeof(IStatusBarSource))]
    [InlineData(typeof(IHistogramDimensionRequestSource))]
    [InlineData(typeof(IActiveFiltersSource))]
    [InlineData(typeof(IEventSelectionSource))]
    [InlineData(typeof(IGroupCollapseNotifier))]
    [InlineData(typeof(ITagBulkUpdateFailedNotifier))]
    [InlineData(typeof(IClearAllFiltersNotifier))]
    [InlineData(typeof(ISetFilterDateRangeSucceededNotifier))]
    [InlineData(typeof(ILibraryEntriesSource))]
    [InlineData(typeof(ILibraryLoadStatusSource))]
    [InlineData(typeof(IFilteredDateRangeSource))]
    [InlineData(typeof(ILoadedLogNamesSource))]
    [InlineData(typeof(IHighlightSelector))]
    [InlineData(typeof(ILogTableColumnDefaultsProvider))]
    [InlineData(typeof(IAppTitleService))]
    [InlineData(typeof(IAttentionBannerService))]
    [InlineData(typeof(IProgressBannerService))]
    [InlineData(typeof(ICriticalErrorService))]
    [InlineData(typeof(IErrorBannerService))]
    [InlineData(typeof(IInfoBannerService))]
    [InlineData(typeof(IAnnouncementService))]
    [InlineData(typeof(IDatabaseService))]
    [InlineData(typeof(IActiveDatabases))]
    [InlineData(typeof(IDatabaseOperationCoordinator))]
    [InlineData(typeof(ILogWatcherService))]
    [InlineData(typeof(ISettingsService))]
    [InlineData(typeof(ITraceLogger))]
    [InlineData(typeof(ILogSourceFactory))]
    [InlineData(typeof(IDebugLogReader))]
    [InlineData(typeof(IOperationLogProgressFactory))]
    [InlineData(typeof(ICurrentVersionProvider))]
    [InlineData(typeof(IDeploymentService))]
    [InlineData(typeof(IGitHubService))]
    [InlineData(typeof(IPackageDeploymentService))]
    [InlineData(typeof(IPackageVersionProvider))]
    [InlineData(typeof(IUpdateService))]
    [InlineData(typeof(BuiltInScenarioRegistry))]
    [InlineData(typeof(IScenarioQueryService))]
    [InlineData(typeof(IScenarioApplyService))]
    [InlineData(typeof(IScenarioLaunchService))]
    [InlineData(typeof(IScenarioAuthoringService))]
    public async Task AddEventLogRuntime_ShouldResolveHostFacingAbstraction(Type serviceType)
    {
        var services = new ServiceCollection();

        services.AddSingleton(Substitute.For<IDispatcher>());
        services.AddSingleton(Substitute.For<IAlertDialogService>());
        services.AddSingleton(Substitute.For<IApplicationRestartService>());
        services.AddSingleton(Substitute.For<ISettingsPreferencesProvider>());
        services.AddSingleton(Substitute.For<IDatabasePreferencesProvider>());
        services.AddSingleton(Substitute.For<IProviderDatabaseMaintenance>());
        services.AddSingleton(Substitute.For<ITitleProvider>());
        services.AddSingleton(Substitute.For<IMainThreadService>());
        services.AddSingleton(Substitute.For<IWindowsIdentityProvider>());
        services.AddSingleton(Substitute.For<IFilePickerService>());
        var filterPaneState = Substitute.For<IState<FilterPaneState>>();
        filterPaneState.Value.Returns(new FilterPaneState());
        services.AddSingleton(filterPaneState);
        var rawEventCountState = Substitute.For<IState<RawEventCountState>>();
        rawEventCountState.Value.Returns(new RawEventCountState());
        services.AddSingleton(rawEventCountState);
        var statusBarState = Substitute.For<IState<StatusBarState>>();
        statusBarState.Value.Returns(new StatusBarState());
        services.AddSingleton(statusBarState);

        var eventLogState = Substitute.For<IState<EventLogState>>();
        eventLogState.Value.Returns(new EventLogState());
        services.AddSingleton(eventLogState);
        var filterLensState = Substitute.For<IState<FilterLensState>>();
        filterLensState.Value.Returns(new FilterLensState());
        services.AddSingleton(filterLensState);
        var logTableState = Substitute.For<IState<LogTableState>>();
        logTableState.Value.Returns(new LogTableState());
        services.AddSingleton(logTableState);
        services.AddSingleton(Substitute.For<IState<RawEventStoreState>>());
        var filteredLogPresenceState = Substitute.For<IState<FilteredLogPresenceState>>();
        filteredLogPresenceState.Value.Returns(new FilteredLogPresenceState());
        services.AddSingleton(filteredLogPresenceState);
        var histogramState = Substitute.For<IState<HistogramState>>();
        histogramState.Value.Returns(new HistogramState());
        services.AddSingleton(histogramState);
        var filterLibraryState = Substitute.For<IState<FilterLibraryState>>();
        filterLibraryState.Value.Returns(new FilterLibraryState());
        services.AddSingleton(filterLibraryState);
        var scenarioFavoritesState = Substitute.For<IState<ScenarioFavoritesState>>();
        scenarioFavoritesState.Value.Returns(new ScenarioFavoritesState());
        services.AddSingleton(scenarioFavoritesState);
        services.AddSingleton(Substitute.For<IStateSelection<EventLogState, bool>>());
        services.AddSingleton(new FileLocationOptions(Path.Combine(Path.GetTempPath(), "EventLogExpertTests")));
        services.AddSingleton<HttpClient>();
        services.AddSingleton(Substitute.For<IMenuActionService>());
        services.AddSingleton(Substitute.For<IFolderPickerService>());
        services.AddSingleton(Substitute.For<IEvtxFolderEnumerator>());

        services.AddEventLogRuntime();

        await using var provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });

        await using var scope = provider.CreateAsyncScope();

        var resolved = scope.ServiceProvider.GetService(serviceType);

        Assert.NotNull(resolved);
    }

    [Fact]
    public async Task AddEventLogRuntime_WhenVerboseResolutionPersistedOff_LeavesResolutionAtShippedThrottle()
    {
        var services = new ServiceCollection();
        var preferences = Substitute.For<ISettingsPreferencesProvider>();
        preferences.VerboseResolutionPreference.Returns(false);
        RegisterHostDependencies(services, preferences);
        services.AddEventLogRuntime();

        await using var provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });

        await using var scope = provider.CreateAsyncScope();

        var routingPolicy = scope.ServiceProvider.GetRequiredService<LogRoutingPolicy>();

        Assert.Equal(LogLevel.Warning, routingPolicy.FileMinimumFor("Resolution"));
    }

    [Fact]
    public async Task AddEventLogRuntime_WhenVerboseResolutionPersistedOn_SeedsResolutionOverrideAtConstruction()
    {
        var services = new ServiceCollection();
        var preferences = Substitute.For<ISettingsPreferencesProvider>();
        preferences.VerboseResolutionPreference.Returns(true);
        RegisterHostDependencies(services, preferences);
        services.AddEventLogRuntime();

        await using var provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });

        await using var scope = provider.CreateAsyncScope();

        var routingPolicy = scope.ServiceProvider.GetRequiredService<LogRoutingPolicy>();

        Assert.Equal(LogLevel.Trace, routingPolicy.FileMinimumFor("Resolution"));
        Assert.Equal(LogLevel.Trace, routingPolicy.FileMinimumFor("Resolution.Modern"));
    }

    [Fact]
    public async Task BothSqliteStores_ShareOneDatabasePath_ResolveIndependently()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"EventLogExpertDualStore_{Guid.NewGuid()}.db");
        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<ITraceLogger>());
        services.AddFilterLibrarySqliteStore(dbPath);
        services.AddScenarioFavoriteSqliteStore(dbPath);

        await using var provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });

        await using var scope = provider.CreateAsyncScope();

        Assert.NotNull(scope.ServiceProvider.GetService<IFilterLibraryStore>());
        Assert.NotNull(scope.ServiceProvider.GetService<IScenarioFavoriteStore>());
    }

    [Fact]
    public async Task Notifiers_ResolveTheirConcreteAndInterfaceAsOneSharedSingleton()
    {
        var services = new ServiceCollection();
        RegisterHostDependencies(services);
        services.AddEventLogRuntime();

        await using var provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });

        Assert.Same(
            provider.GetRequiredService<GroupCollapseNotifier>(),
            provider.GetRequiredService<IGroupCollapseNotifier>());
        Assert.Same(
            provider.GetRequiredService<TagBulkUpdateFailedNotifier>(),
            provider.GetRequiredService<ITagBulkUpdateFailedNotifier>());
        Assert.Same(
            provider.GetRequiredService<ClearAllFiltersNotifier>(),
            provider.GetRequiredService<IClearAllFiltersNotifier>());
        Assert.Same(
            provider.GetRequiredService<SetFilterDateRangeSucceededNotifier>(),
            provider.GetRequiredService<ISetFilterDateRangeSucceededNotifier>());
    }

    private static void RegisterHostDependencies(IServiceCollection services, ISettingsPreferencesProvider? preferences = null)
    {
        services.AddSingleton(Substitute.For<IDispatcher>());
        services.AddSingleton(Substitute.For<IAlertDialogService>());
        services.AddSingleton(Substitute.For<IApplicationRestartService>());
        services.AddSingleton(preferences ?? Substitute.For<ISettingsPreferencesProvider>());
        services.AddSingleton(Substitute.For<IDatabasePreferencesProvider>());
        services.AddSingleton(Substitute.For<IProviderDatabaseMaintenance>());
        services.AddSingleton(Substitute.For<ITitleProvider>());
        services.AddSingleton(Substitute.For<IMainThreadService>());
        services.AddSingleton(Substitute.For<IWindowsIdentityProvider>());
        services.AddSingleton(Substitute.For<IFilePickerService>());
        services.AddSingleton(Substitute.For<IState<EventLogState>>());
        services.AddSingleton(Substitute.For<IState<FilterPaneState>>());
        services.AddSingleton(Substitute.For<IState<RawEventCountState>>());
        services.AddSingleton(Substitute.For<IState<StatusBarState>>());
        services.AddSingleton(Substitute.For<IState<FilterLensState>>());
        services.AddSingleton(Substitute.For<IState<LogTableState>>());
        services.AddSingleton(Substitute.For<IState<RawEventStoreState>>());
        services.AddSingleton(Substitute.For<IState<FilteredLogPresenceState>>());
        services.AddSingleton(Substitute.For<IState<HistogramState>>());
        services.AddSingleton(Substitute.For<IState<FilterLibraryState>>());
        services.AddSingleton(Substitute.For<IState<ScenarioFavoritesState>>());
        services.AddSingleton(Substitute.For<IStateSelection<EventLogState, bool>>());
        services.AddSingleton(new FileLocationOptions(Path.Combine(Path.GetTempPath(), "EventLogExpertTests")));
        services.AddSingleton<HttpClient>();
        services.AddSingleton(Substitute.For<IMenuActionService>());
        services.AddSingleton(Substitute.For<IFolderPickerService>());
        services.AddSingleton(Substitute.For<IEvtxFolderEnumerator>());
    }
}
