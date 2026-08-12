// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.Common.Sources;
using Fluxor;
using Microsoft.Extensions.DependencyInjection;

namespace EventLogExpert.Runtime.EventLog;

internal sealed class FilterAppliedSource(
    IState<EventLogState> state,
    [FromKeyedServices(LogCategories.EventLog)] ITraceLogger logger)
    : ObservableStateSourceBase<EventLogState, bool>(state, logger, static state => state.AppliedFilter.IsFilteringEnabled),
        IFilterAppliedSource
{
    public bool IsFilteringEnabled => CurrentProjection;
}
