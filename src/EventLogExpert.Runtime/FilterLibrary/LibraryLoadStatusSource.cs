// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.Common.Sources;
using Fluxor;
using Microsoft.Extensions.DependencyInjection;

namespace EventLogExpert.Runtime.FilterLibrary;

internal sealed class LibraryLoadStatusSource(
    IState<FilterLibraryState> state,
    [FromKeyedServices(LogCategories.EventLog)] ITraceLogger logger)
    : ObservableStateSourceBase<FilterLibraryState, LibraryLoadStatus>(state,
            logger,
            static state => new LibraryLoadStatus(state.IsLoaded, state.LoadError)),
        ILibraryLoadStatusSource
{
    public LibraryLoadStatus Current => CurrentProjection;
}
