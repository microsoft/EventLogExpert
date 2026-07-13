// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Fluxor;
using System.Collections.Immutable;

namespace EventLogExpert.Runtime.FilterLenses;

/// <summary>
///     The active transient lens stack layered over the persistent filter. Oldest first; the last entry is the top of
///     the stack (most recently pushed). Lenses live only here and in the derived applied filter - never in the persistent
///     <c>FilterPaneState</c> - so they cannot contaminate saved filters.
/// </summary>
[FeatureState]
public sealed record FilterLensState
{
    public ImmutableList<FilterLens> Lenses { get; init; } = [];
}
