// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Compilation;
using EventLogExpert.Filtering.Persistence;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.Alerts;
using EventLogExpert.Runtime.Common.Clipboard;
using EventLogExpert.Runtime.EventLog;
using EventLogExpert.Runtime.FilterLenses;
using EventLogExpert.Runtime.FilterPane;
using EventLogExpert.Runtime.LogTable;
using EventLogExpert.UI.LogTable.Find;
using EventLogExpert.UI.Menu;
using Fluxor;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using System.Collections.Immutable;

namespace EventLogExpert.UI.Tests.TestUtils;

internal static class LogTablePaneDependenciesExtensions
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddLogTablePaneDependencies()
        {
            services.AddSingleton(Substitute.For<IAlertDialogService>());
            services.AddSingleton(Substitute.For<IClipboardService>());
            services.AddSingleton(Substitute.For<IFilterLensCommands>());
            services.AddSingleton(Substitute.For<IFilterPaneCommands>());
            services.AddSingleton(Substitute.For<IFilterService>());
            services.AddSingleton<IFindCoordinator, FindCoordinator>();
            services.AddSingleton<IFindMarkerSource, FindMarkerSource>();
            services.AddSingleton(Substitute.For<ILogTableCommands>());
            services.AddSingleton(Substitute.For<IMenuService>());
            services.AddSingleton(Substitute.For<ITraceLogger>());

            services.AddKeyedSingleton(LogCategories.EventLog, Substitute.For<ITraceLogger>());
            services.AddSingleton<IOrderedViewSource, OrderedViewSource>();
            services.AddSingleton<DisplayIndicatorGate>();

            services.AddSingleton<ILogTableQueries, LogTableQueries>();
            services.AddSingleton<ILogTabBarSource, LogTabBarSource>();

            var activeFilters = Substitute.For<IActiveFiltersSource>();
            activeFilters.Current.Returns(ImmutableList<SavedFilter>.Empty);
            services.AddSingleton(activeFilters);

            var eventFocus = Substitute.For<IEventFocusSource>();
            eventFocus.Current.Returns((SelectionEntry?)null);
            services.AddSingleton(eventFocus);

            var eventSelection = Substitute.For<IEventSelectionSource>();
            eventSelection.Current.Returns(ImmutableList<SelectionEntry>.Empty);
            services.AddSingleton(eventSelection);

            services.AddSingleton(Substitute.For<IGroupCollapseNotifier>());

            var revealFocus = Substitute.For<IRevealFocusSource>();
            revealFocus.Current.Returns((RevealFocusRequest?)null);
            services.AddSingleton(revealFocus);

            var presenceState = Substitute.For<IState<FilteredLogPresenceState>>();
            presenceState.Value.Returns(new FilteredLogPresenceState());
            services.AddSingleton(presenceState);

            return services;
        }
    }
}
