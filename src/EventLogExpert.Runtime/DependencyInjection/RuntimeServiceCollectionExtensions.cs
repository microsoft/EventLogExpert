// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Databases;
using EventLogExpert.Eventing.Logging;
using EventLogExpert.Runtime.Alerts;
using EventLogExpert.Runtime.Banner;
using EventLogExpert.Runtime.Common.AppTitle;
using EventLogExpert.Runtime.Common.Versioning;
using EventLogExpert.Runtime.Database;
using EventLogExpert.Runtime.DebugLog;
using EventLogExpert.Runtime.EventLog;
using EventLogExpert.Runtime.FilterCache;
using EventLogExpert.Runtime.FilterGroup;
using EventLogExpert.Runtime.FilterPane;
using EventLogExpert.Filtering.Services;
using EventLogExpert.Runtime.LogTable;
using EventLogExpert.Runtime.Menu;
using EventLogExpert.Runtime.Modal;
using EventLogExpert.Runtime.Settings;
using EventLogExpert.Runtime.Update;
using EventLogExpert.Runtime.Update.Deployment;
using Effects = EventLogExpert.Runtime.EventLog.Effects;

namespace Microsoft.Extensions.DependencyInjection;

public static class RuntimeServiceCollectionExtensions
{
    /// <summary>
    ///     Registers the runtime tier's services. Callers MUST also call <see cref="FilteringServiceCollectionExtensions.AddEventLogFiltering" />
    ///     before resolving the resulting provider — the runtime tier's Fluxor-scanned <c>EventLog.Effects</c> class
    ///     depends on <c>IFilterService</c>, which is owned by the filtering tier. Omitting it produces a hard-to-diagnose
    ///     DI resolution failure at the first action dispatch.
    /// </summary>
    public static IServiceCollection AddEventLogRuntime(this IServiceCollection services)
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
