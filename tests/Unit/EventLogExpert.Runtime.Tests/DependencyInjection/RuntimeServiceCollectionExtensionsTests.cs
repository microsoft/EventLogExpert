// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Provider.Maintenance;
using EventLogExpert.Provider.Resolution;
using EventLogExpert.Runtime.Alerts;
using EventLogExpert.Runtime.Banner;
using EventLogExpert.Runtime.Common.AppTitle;
using EventLogExpert.Runtime.Common.Files;
using EventLogExpert.Runtime.Common.Identity;
using EventLogExpert.Runtime.Common.Restart;
using EventLogExpert.Runtime.Common.Threading;
using EventLogExpert.Runtime.Common.Versioning;
using EventLogExpert.Runtime.Database;
using EventLogExpert.Runtime.EventLog;
using EventLogExpert.Runtime.FilterCache;
using EventLogExpert.Runtime.FilterGroup;
using EventLogExpert.Runtime.FilterPane;
using EventLogExpert.Runtime.LogTable;
using EventLogExpert.Runtime.Menu;
using EventLogExpert.Runtime.Modal;
using EventLogExpert.Runtime.Settings;
using EventLogExpert.Runtime.Update;
using EventLogExpert.Runtime.Update.Deployment;
using Fluxor;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace EventLogExpert.Runtime.Tests.DependencyInjection;

public sealed class RuntimeServiceCollectionExtensionsTests
{
    [Theory]
    // Command facades.
    [InlineData(typeof(IEventLogCommands))]
    [InlineData(typeof(IFilterCacheCommands))]
    [InlineData(typeof(IFilterGroupCommands))]
    [InlineData(typeof(IFilterPaneCommands))]
    [InlineData(typeof(ILogTableCommands))]
    // UI capabilities.
    [InlineData(typeof(IHighlightSelector))]
    [InlineData(typeof(ILogTableColumnDefaultsProvider))]
    // ILogReloadCoordinator omitted — it forwards to the Fluxor-registered Effects type,
    // which is auto-registered by AddFluxor (not by AddEventLogRuntime). Production wiring
    // covers it; here it would require also bootstrapping Fluxor scanning in the test.
    // Application services (moved out of MauiProgram into AddEventLogRuntime).
    [InlineData(typeof(IAppTitleService))]
    [InlineData(typeof(IBannerService))]
    [InlineData(typeof(IDatabaseService))]
    [InlineData(typeof(IActiveDatabases))]
    [InlineData(typeof(IInlineAlertHostBroker))]
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
        services.AddSingleton(Substitute.For<IState<EventLogState>>());
        services.AddSingleton(Substitute.For<IStateSelection<EventLogState, bool>>());
        services.AddSingleton(new FileLocationOptions(Path.Combine(Path.GetTempPath(), "EventLogExpertTests")));
        services.AddSingleton<HttpClient>();

        services.AddEventLogRuntime();

        await using var provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });

        await using var scope = provider.CreateAsyncScope();

        // Act
        var resolved = scope.ServiceProvider.GetService(serviceType);

        // Assert
        Assert.NotNull(resolved);
    }
}
