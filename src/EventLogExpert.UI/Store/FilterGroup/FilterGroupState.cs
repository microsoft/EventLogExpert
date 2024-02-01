// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Models;
using Fluxor;
using System.Collections.Immutable;
using System.Collections.ObjectModel;

namespace EventLogExpert.UI.Store.FilterGroup;

[FeatureState]
public sealed record FilterGroupState
{
    public ImmutableList<FilterGroupModel> Groups { get; init; } = [];

    public ReadOnlyDictionary<string, FilterGroupData> DisplayGroups { get; init; } =
        new Dictionary<string, FilterGroupData>().AsReadOnly();
}
