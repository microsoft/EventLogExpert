// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.DatabaseTools.Common.Operations;
using EventLogExpert.Logging.Abstractions;
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
using EventLogExpert.Runtime.EventLog;
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
using Fluxor;
using Microsoft.Extensions.DependencyInjection;
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
        services.AddSingleton(Substitute.For<ITraceLogger>());
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
        // Arrange
        var services = new ServiceCollection();
        RegisterHostDependencies(services);
        services.AddEventLogRuntime();

        await using var provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });

        // Resolve from TWO DIFFERENT SCOPES — singleton instances are shared across scopes,
        // scoped instances would be distinct. Resolving twice from the same scope cannot
        // distinguish singleton from scoped (both would return the same per-scope instance).
        await using var scope1 = provider.CreateAsyncScope();
        await using var scope2 = provider.CreateAsyncScope();

        // Act
        var first = scope1.ServiceProvider.GetService(facetType);
        var second = scope2.ServiceProvider.GetService(facetType);

        // Assert
        Assert.NotNull(first);
        Assert.Same(first, second);
    }

    [Fact]
    public async Task AddEventLogRuntime_Resolves5BannerFacetsToSameSingleton()
    {
        // Arrange
        var services = new ServiceCollection();
        RegisterHostDependencies(services);
        services.AddEventLogRuntime();

        await using var provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });

        // Resolve from two scopes — locks in same-instance + singleton-lifetime invariants
        // in one test. If any facet were registered Scoped instead of Singleton, the
        // cross-scope assertion would fail (per-scope instances would be distinct).
        await using var scope1 = provider.CreateAsyncScope();
        await using var scope2 = provider.CreateAsyncScope();

        // Act
        var attentionScope1 = scope1.ServiceProvider.GetRequiredService<IAttentionBannerService>();
        var progressScope1 = scope1.ServiceProvider.GetRequiredService<IProgressBannerService>();
        var criticalScope1 = scope1.ServiceProvider.GetRequiredService<ICriticalErrorService>();
        var errorScope1 = scope1.ServiceProvider.GetRequiredService<IErrorBannerService>();
        var infoScope1 = scope1.ServiceProvider.GetRequiredService<IInfoBannerService>();
        var attentionScope2 = scope2.ServiceProvider.GetRequiredService<IAttentionBannerService>();

        // Assert — all five facets resolve to the same backing BannerService instance
        // within a scope, AND that instance is shared across scopes (singleton lifetime).
        Assert.Same(attentionScope1, progressScope1);
        Assert.Same(attentionScope1, criticalScope1);
        Assert.Same(attentionScope1, errorScope1);
        Assert.Same(attentionScope1, infoScope1);
        Assert.Same(attentionScope1, attentionScope2);
    }

    [Theory]
    // Command facades.
    [InlineData(typeof(IEventLogCommands))]
    [InlineData(typeof(IFilterLibraryCommands))]
    [InlineData(typeof(IFilterPaneCommands))]
    [InlineData(typeof(ILogTableCommands))]
    [InlineData(typeof(IScenarioFavoriteCommands))]
    [InlineData(typeof(IEventLogQueries))]
    // UI capabilities.
    [InlineData(typeof(IHighlightSelector))]
    [InlineData(typeof(ILogTableColumnDefaultsProvider))]
    // ILogReloadCoordinator omitted — it forwards to the Fluxor-registered Effects type,
    // which is auto-registered by AddFluxor (not by AddEventLogRuntime). Production wiring
    // covers it; here it would require also bootstrapping Fluxor scanning in the test.
    // Application services (moved out of MauiProgram into AddEventLogRuntime).
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
    [InlineData(typeof(IModalCoordinator))]
    [InlineData(typeof(ILogWatcherService))]
    [InlineData(typeof(IMenuService))]
    [InlineData(typeof(IModalService))]
    [InlineData(typeof(ISettingsService))]
    // Update + deployment services.
    [InlineData(typeof(ICurrentVersionProvider))]
    [InlineData(typeof(IDeploymentService))]
    [InlineData(typeof(IGitHubService))]
    [InlineData(typeof(IPackageDeploymentService))]
    [InlineData(typeof(IPackageVersionProvider))]
    [InlineData(typeof(IUpdateService))]
    // Built-in scenarios.
    [InlineData(typeof(BuiltInScenarioRegistry))]
    [InlineData(typeof(IScenarioQueryService))]
    [InlineData(typeof(IScenarioApplyService))]
    [InlineData(typeof(IScenarioLaunchService))]
    [InlineData(typeof(IScenarioAuthoringService))]
    public async Task AddEventLogRuntime_ShouldResolveHostFacingAbstraction(Type serviceType)
    {
        // Arrange
        var services = new ServiceCollection();

        // Dependencies the host normally provides (IDispatcher comes from Fluxor; the
        // others are concrete options/identity/IO surfaces that AddEventLogRuntime doesn't
        // own). Substituting them keeps this smoke test focused on the wiring inside
        // AddEventLogRuntime while letting the dependency-injection container actually
        // build each registered service.
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
        services.AddSingleton(Substitute.For<IState<EventLogState>>());
        services.AddSingleton(Substitute.For<IState<FilterPaneState>>());
        services.AddSingleton(Substitute.For<IState<RawEventStoreState>>());
        services.AddSingleton(Substitute.For<IStateSelection<EventLogState, bool>>());
        services.AddSingleton(new FileLocationOptions(Path.Combine(Path.GetTempPath(), "EventLogExpertTests")));
        services.AddSingleton<HttpClient>();
        services.AddSingleton(Substitute.For<IMenuActionService>());

        services.AddEventLogRuntime();

        await using var provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });

        await using var scope = provider.CreateAsyncScope();

        // Act
        var resolved = scope.ServiceProvider.GetService(serviceType);

        // Assert
        Assert.NotNull(resolved);
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

    private static void RegisterHostDependencies(IServiceCollection services)
    {
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
        services.AddSingleton(Substitute.For<IState<EventLogState>>());
        services.AddSingleton(Substitute.For<IState<FilterPaneState>>());
        services.AddSingleton(Substitute.For<IState<RawEventStoreState>>());
        services.AddSingleton(Substitute.For<IStateSelection<EventLogState, bool>>());
        services.AddSingleton(new FileLocationOptions(Path.Combine(Path.GetTempPath(), "EventLogExpertTests")));
        services.AddSingleton<HttpClient>();
        services.AddSingleton(Substitute.For<IMenuActionService>());
    }
}
