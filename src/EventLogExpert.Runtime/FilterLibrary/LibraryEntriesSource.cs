// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.Common.Sources;
using Fluxor;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Immutable;

namespace EventLogExpert.Runtime.FilterLibrary;

internal sealed class LibraryEntriesSource(
    IState<FilterLibraryState> state,
    [FromKeyedServices(LogCategories.EventLog)] ITraceLogger logger)
    : ObservableStateSourceBase<FilterLibraryState, ImmutableList<LibraryEntry>>(
            state,
            logger,
            static state => state.Entries,
            static (next, current) => ReferenceEquals(next, current)),
        ILibraryEntriesSource
{
    public ImmutableList<LibraryEntry> Current => CurrentProjection;
}
