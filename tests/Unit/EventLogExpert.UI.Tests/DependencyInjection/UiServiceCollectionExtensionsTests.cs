// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Databases;
using EventLogExpert.UI.Alerts;
using EventLogExpert.UI.Banner;
using EventLogExpert.UI.Common.AppTitle;
using EventLogExpert.UI.Common.Files;
using EventLogExpert.UI.Common.Identity;
using EventLogExpert.UI.Common.Restart;
using EventLogExpert.UI.Common.Threading;
using EventLogExpert.UI.Common.Versioning;
using EventLogExpert.UI.Database;
using EventLogExpert.UI.DependencyInjection;
using EventLogExpert.UI.EventLog;
using EventLogExpert.UI.FilterCache;
using EventLogExpert.UI.FilterGroup;
using EventLogExpert.UI.FilterPane;
using EventLogExpert.UI.Filters;
using EventLogExpert.UI.LogTable;
using EventLogExpert.UI.Menu;
using EventLogExpert.UI.Modal;
using EventLogExpert.UI.Settings;
using EventLogExpert.UI.Update;
using EventLogExpert.UI.Update.Deployment;
using Fluxor;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace EventLogExpert.UI.Tests.DependencyInjection;

public sealed class UiServiceCollectionExtensionsTests
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
    // which is auto-registered by AddFluxor (not by RegisterUiLibrary). Production wiring
    // covers it; here it would require also bootstrapping Fluxor scanning in the test.
    // Application services (moved out of MauiProgram into RegisterUiLibrary).
    [InlineData(typeof(IAppTitleService))]
    [InlineData(typeof(IBannerService))]
    [InlineData(typeof(IDatabaseService))]
    [InlineData(typeof(IActiveDatabasePathsProvider))]
    [InlineData(typeof(IFilterService))]
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
    public async Task RegisterUiLibrary_ShouldResolveHostFacingAbstraction(Type serviceType)
    {
        // Arrange
        var services = new ServiceCollection();

        // Dependencies the host normally provides (IDispatcher comes from Fluxor; the
        // others are concrete options/identity/IO surfaces that RegisterUiLibrary doesn't
        // own). Substituting them keeps this smoke test focused on the wiring inside
        // RegisterUiLibrary while letting the dependency-injection container actually
        // build each registered service.
        services.AddSingleton(Substitute.For<IDispatcher>());
        services.AddSingleton(Substitute.For<IAlertDialogService>());
        services.AddSingleton(Substitute.For<IApplicationRestartService>());
        services.AddSingleton(Substitute.For<ISettingsPreferencesProvider>());
        services.AddSingleton(Substitute.For<IDatabasePreferencesProvider>());
        services.AddSingleton(Substitute.For<ITitleProvider>());
        services.AddSingleton(Substitute.For<IMainThreadService>());
        services.AddSingleton(Substitute.For<IWindowsIdentityProvider>());
        services.AddSingleton(Substitute.For<IState<EventLogState>>());
        services.AddSingleton(Substitute.For<IStateSelection<EventLogState, bool>>());
        services.AddSingleton(new FileLocationOptions(Path.Combine(Path.GetTempPath(), "EventLogExpertTests")));
        services.AddSingleton<HttpClient>();

        services.RegisterUiLibrary();
        await using var provider = services.BuildServiceProvider();

        // Act
        var resolved = provider.GetService(serviceType);

        // Assert
        Assert.NotNull(resolved);
    }
}
