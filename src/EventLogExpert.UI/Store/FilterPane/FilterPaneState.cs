﻿// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Models;
using Fluxor;
using System.Collections.Immutable;

namespace EventLogExpert.UI.Store.FilterPane;

[FeatureState]
public sealed record FilterPaneState
{
    public IImmutableList<FilterModel> CurrentFilters { get; init; } = [];

    public IImmutableList<FilterModel> CachedFilters { get; init; } = [];

    public FilterDateModel? FilteredDateRange { get; init; }

    public FilterModel? AdvancedFilter { get; init; }

    public bool IsLoading { get; init; }
}
