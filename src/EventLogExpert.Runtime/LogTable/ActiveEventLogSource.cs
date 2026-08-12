// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.EventLogs;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.Common.Sources;
using Fluxor;
using Microsoft.Extensions.DependencyInjection;

namespace EventLogExpert.Runtime.LogTable;

internal sealed class ActiveEventLogSource(
    IState<LogTableState> state,
    [FromKeyedServices(LogCategories.EventLog)] ITraceLogger logger)
    : ObservableStateSourceBase<LogTableState, EventLogId?>(state, logger, static state => state.ActiveEventLogId),
        IActiveEventLogSource
{
    public EventLogId? Current => CurrentProjection;
}
