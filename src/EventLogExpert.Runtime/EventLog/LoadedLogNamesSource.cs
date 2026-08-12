// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.Common.Sources;
using Fluxor;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Immutable;

namespace EventLogExpert.Runtime.EventLog;

internal sealed class LoadedLogNamesSource(
    IState<EventLogState> state,
    [FromKeyedServices(LogCategories.EventLog)] ITraceLogger logger)
    : ObservableStateSourceBase<EventLogState, ImmutableHashSet<string>>(
            state,
            logger,
            static state => state.LoadedLogNames,
            static (next, current) => ReferenceEquals(next, current)),
        ILoadedLogNamesSource
{
    public ImmutableHashSet<string> Current => CurrentProjection;
}
