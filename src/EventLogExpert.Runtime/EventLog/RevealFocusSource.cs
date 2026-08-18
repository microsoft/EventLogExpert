// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.Common.Sources;
using Fluxor;
using Microsoft.Extensions.DependencyInjection;

namespace EventLogExpert.Runtime.EventLog;

internal sealed class RevealFocusSource(
    IState<EventLogState> state,
    [FromKeyedServices(LogCategories.EventLog)] ITraceLogger logger)
    : ObservableStateSourceBase<EventLogState, RevealFocusRequest?>(state, logger, static state => state.PendingRevealFocus),
        IRevealFocusSource
{
    public RevealFocusRequest? Current => CurrentProjection;
}
