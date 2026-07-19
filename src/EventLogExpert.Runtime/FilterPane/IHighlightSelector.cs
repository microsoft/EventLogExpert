// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Persistence;
using System.Collections.Immutable;

namespace EventLogExpert.Runtime.FilterPane;

public interface IHighlightSelector
{
    int ComputeHighlightKey(ImmutableList<SavedFilter> filters);

    int ComputePredicatePlanKey(ImmutableList<SavedFilter> filters);

    SavedFilter[] Select(ImmutableList<SavedFilter> filters);
}
