// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Persistence;
using EventLogExpert.Runtime.Common.Sources;
using System.Collections.Immutable;

namespace EventLogExpert.Runtime.FilterPane;

public interface IActiveFiltersSource : IChangeNotifier
{
    ImmutableList<SavedFilter> Current { get; }
}
