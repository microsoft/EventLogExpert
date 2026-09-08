// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.Common.Sources;
using Fluxor;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Immutable;

namespace EventLogExpert.Runtime.FilterLenses;

internal sealed class FilterLensSource(
    IState<FilterLensState> state,
    [FromKeyedServices(LogCategories.EventLog)] ITraceLogger logger)
    : ObservableStateSourceBase<FilterLensState, ImmutableList<FilterLensSummary>>(
            state,
            logger,
            static state => [.. state.Lenses.Select(lens => new FilterLensSummary(lens.Id, lens.Label, lens.Kind))],
            static (next, current) => next.SequenceEqual(current)),
        IFilterLensSource
{
    public ImmutableList<FilterLensSummary> Lenses => CurrentProjection;
}
