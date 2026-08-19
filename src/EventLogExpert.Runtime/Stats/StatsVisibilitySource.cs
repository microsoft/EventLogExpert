// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.Common.Sources;
using Fluxor;
using Microsoft.Extensions.DependencyInjection;

namespace EventLogExpert.Runtime.Stats;

internal sealed class StatsVisibilitySource(
    IState<StatsState> state,
    [FromKeyedServices(LogCategories.EventLog)] ITraceLogger logger)
    : ObservableStateSourceBase<StatsState, bool>(state, logger, static state => state.IsVisible),
        IStatsVisibilitySource
{
    public bool IsVisible => CurrentProjection;
}
