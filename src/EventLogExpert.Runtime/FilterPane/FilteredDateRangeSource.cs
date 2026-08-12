// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Evaluation;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.Common.Sources;
using Fluxor;
using Microsoft.Extensions.DependencyInjection;

namespace EventLogExpert.Runtime.FilterPane;

internal sealed class FilteredDateRangeSource(
    IState<FilterPaneState> state,
    [FromKeyedServices(LogCategories.EventLog)] ITraceLogger logger)
    : ObservableStateSourceBase<FilterPaneState, DateFilter?>(state, logger, static state => state.FilteredDateRange),
        IFilteredDateRangeSource
{
    public DateFilter? Current => CurrentProjection;
}
