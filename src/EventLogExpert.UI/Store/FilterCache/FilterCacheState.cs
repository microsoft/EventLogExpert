// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Models;
using Fluxor;
using System.Collections.Immutable;

namespace EventLogExpert.UI.Store.FilterCache;

[FeatureState]
public record FilterCacheState
{
    public ImmutableList<FilterCacheModel> Filters { get; init; } = ImmutableList<FilterCacheModel>.Empty;
}
