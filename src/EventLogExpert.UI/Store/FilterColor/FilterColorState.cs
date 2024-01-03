// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Models;
using Fluxor;
using System.Collections.Immutable;

namespace EventLogExpert.UI.Store.FilterColor;

[FeatureState]
public sealed record FilterColorState
{
    public ImmutableList<FilterColorModel> Filters { get; init; } = [];
}
