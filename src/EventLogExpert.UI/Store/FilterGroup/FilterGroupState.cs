// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Models;
using Fluxor;
using System.Collections.Immutable;

namespace EventLogExpert.UI.Store.FilterGroup;

[FeatureState]
public sealed record FilterGroupState
{
    public ImmutableList<FilterGroupModel> Groups { get; init; } = [];
}
