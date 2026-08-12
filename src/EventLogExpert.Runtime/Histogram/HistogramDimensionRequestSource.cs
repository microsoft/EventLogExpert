// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.Common.Sources;
using Fluxor;
using Microsoft.Extensions.DependencyInjection;

namespace EventLogExpert.Runtime.Histogram;

internal sealed class HistogramDimensionRequestSource(
    IState<HistogramState> state,
    [FromKeyedServices(LogCategories.EventLog)] ITraceLogger logger)
    : ObservableStateSourceBase<HistogramState, HistogramDimensionRequest?>(state,
            logger,
            static state => state.DimensionRequest),
        IHistogramDimensionRequestSource
{
    public HistogramDimensionRequest? Current => CurrentProjection;
}
