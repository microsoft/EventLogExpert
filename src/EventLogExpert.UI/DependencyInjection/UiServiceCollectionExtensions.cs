// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.EventLog;
using EventLogExpert.UI.FilterGroup;
using EventLogExpert.UI.FilterPane;
using EventLogExpert.UI.LogTable;
using Microsoft.Extensions.DependencyInjection;

namespace EventLogExpert.UI.DependencyInjection;

/// <summary>
///     Composition-root extension for registering the UI library's host-facing intent and
///     capability APIs. Lets the MAUI head consume <see cref="EventLogExpert.UI" /> without
///     needing <c>InternalsVisibleTo</c> on its internal facade implementations.
/// </summary>
public static class UiServiceCollectionExtensions
{
    /// <summary>
    ///     Registers the public host-facing intent and capability APIs exposed by
    ///     <see cref="EventLogExpert.UI" />. Implementations are <c>internal sealed</c> per
    ///     least-privilege; this extension is the only public entry point for the host to wire
    ///     them up.
    /// </summary>
    public static IServiceCollection RegisterUiLibrary(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IEventLogCommands, EventLogCommands>();
        services.AddSingleton<IFilterPaneCommands, FilterPaneCommands>();
        services.AddSingleton<IFilterGroupCommands, FilterGroupCommands>();
        services.AddSingleton<ILogTableCommands, LogTableCommands>();
        services.AddSingleton<IHighlightSelector, HighlightSelector>();
        services.AddSingleton<ILogTableColumnDefaultsProvider, ColumnDefaults>();

        return services;
    }
}
