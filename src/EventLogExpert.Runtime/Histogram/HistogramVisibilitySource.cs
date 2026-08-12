// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.Common.Sources;
using Fluxor;
using Microsoft.Extensions.DependencyInjection;

namespace EventLogExpert.Runtime.Histogram;

internal sealed class HistogramVisibilitySource(
    IState<HistogramState> state,
    [FromKeyedServices(LogCategories.EventLog)] ITraceLogger logger)
    : ObservableStateSourceBase<HistogramState, bool>(state, logger, static state => state.IsVisible),
        IHistogramVisibilitySource
{
    public bool IsVisible => CurrentProjection;
}
