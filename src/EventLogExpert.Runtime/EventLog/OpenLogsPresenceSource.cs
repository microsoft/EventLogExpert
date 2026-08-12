// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.Common.Sources;
using Fluxor;
using Microsoft.Extensions.DependencyInjection;

namespace EventLogExpert.Runtime.EventLog;

internal sealed class OpenLogsPresenceSource(
    IState<EventLogState> state,
    [FromKeyedServices(LogCategories.EventLog)] ITraceLogger logger)
    : ObservableStateSourceBase<EventLogState, bool>(state, logger, static state => state.OpenLogCount > 0),
        IOpenLogsPresenceSource
{
    public bool HasOpenLogs => CurrentProjection;
}
