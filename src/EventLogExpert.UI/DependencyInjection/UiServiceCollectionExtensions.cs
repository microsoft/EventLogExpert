// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Databases;
using EventLogExpert.Eventing.Logging;
using EventLogExpert.UI.Alerts;
using EventLogExpert.UI.Banner;
using EventLogExpert.UI.Common.AppTitle;
using EventLogExpert.UI.Common.Versioning;
using EventLogExpert.UI.Database;
using EventLogExpert.UI.DebugLog;
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
using Microsoft.Extensions.DependencyInjection;
using Effects = EventLogExpert.UI.EventLog.Effects;

namespace EventLogExpert.UI.DependencyInjection;

/// <summary>
///     Composition-root extension for registering the UI library's host-facing intent and capability APIs. Lets the
///     MAUI head consume <see cref="EventLogExpert.UI" /> without needing <c>InternalsVisibleTo</c> on its internal facade
///     implementations.
/// </summary>
public static class UiServiceCollectionExtensions
{
    /// <summary>
    ///     Registers the public host-facing intent and capability APIs exposed by <see cref="EventLogExpert.UI" />.
    ///     Implementations are <c>internal sealed</c> per least-privilege; this extension is the only public entry point for
    ///     the host to wire them up.
    /// </summary>
    public static IServiceCollection RegisterUiLibrary(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Command facades.
        services.AddSingleton<IEventLogCommands, EventLogCommands>();
        services.AddSingleton<IFilterCacheCommands, FilterCacheCommands>();
        services.AddSingleton<IFilterGroupCommands, FilterGroupCommands>();
        services.AddSingleton<IFilterPaneCommands, FilterPaneCommands>();
        services.AddSingleton<ILogTableCommands, LogTableCommands>();

        // UI capabilities.
        services.AddSingleton<IHighlightSelector, HighlightSelector>();
        services.AddSingleton<ILogTableColumnDefaultsProvider, ColumnDefaults>();
        services.AddSingleton<ILogReloadCoordinator>(static sp => sp.GetRequiredService<Effects>());

        // Application services.
        services.AddSingleton<IAppTitleService, AppTitleService>();
        services.AddSingleton<IBannerService, BannerService>();
        services.AddSingleton<DatabaseService>();
        services.AddSingleton<IDatabaseService>(static sp => sp.GetRequiredService<DatabaseService>());
        services.AddSingleton<IActiveDatabasePathsProvider>(static sp => sp.GetRequiredService<DatabaseService>());
        services.AddSingleton<DebugLogService>();
        services.AddSingleton<ITraceLogger>(static sp => sp.GetRequiredService<DebugLogService>());
        services.AddSingleton<IFileLogger>(static sp => sp.GetRequiredService<DebugLogService>());
        services.AddSingleton<IFilterService, FilterService>();
        services.AddSingleton<IInlineAlertHostBroker, InlineAlertHostBroker>();
        services.AddSingleton<ILogWatcherService, LogWatcherService>();
        services.AddSingleton<IMenuService, MenuService>();
        services.AddSingleton<IModalService, ModalService>();
        services.AddSingleton<ISettingsService, SettingsService>();

        // Update + deployment services.
        services.AddSingleton<ICurrentVersionProvider, CurrentVersionProvider>();
        services.AddSingleton<IDeploymentService, DeploymentService>();
        services.AddSingleton<IGitHubService, GitHubService>();
        services.AddSingleton<IPackageDeploymentService, PackageDeploymentService>();
        services.AddSingleton<IPackageVersionProvider, PackageVersionProvider>();
        services.AddSingleton<IUpdateService, UpdateService>();

        return services;
    }
}
