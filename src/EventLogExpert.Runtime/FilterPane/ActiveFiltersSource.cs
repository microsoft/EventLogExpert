// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Persistence;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.Common.Sources;
using Fluxor;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Immutable;

namespace EventLogExpert.Runtime.FilterPane;

internal sealed class ActiveFiltersSource(
    IState<FilterPaneState> state,
    [FromKeyedServices(LogCategories.EventLog)] ITraceLogger logger)
    : ObservableStateSourceBase<FilterPaneState, ImmutableList<SavedFilter>>(
            state,
            logger,
            static state => state.Filters,
            static (next, current) => ReferenceEquals(next, current)),
        IActiveFiltersSource
{
    public ImmutableList<SavedFilter> Current => CurrentProjection;
}
