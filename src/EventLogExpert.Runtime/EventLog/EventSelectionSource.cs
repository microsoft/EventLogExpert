// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.Common.Sources;
using Fluxor;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Immutable;

namespace EventLogExpert.Runtime.EventLog;

internal sealed class EventSelectionSource(
    IState<EventLogState> state,
    [FromKeyedServices(LogCategories.EventLog)] ITraceLogger logger)
    : ObservableStateSourceBase<EventLogState, ImmutableList<SelectionEntry>>(
            state,
            logger,
            static state => state.Selection,
            static (next, current) => ReferenceEquals(next, current)),
        IEventSelectionSource
{
    public ImmutableList<SelectionEntry> Current => CurrentProjection;
}
