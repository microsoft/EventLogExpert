// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Evaluation;
using EventLogExpert.Filtering.Persistence;
using System.Collections.Immutable;

namespace EventLogExpert.Runtime.FilterLenses;

/// <summary>
///     A transient, reversible narrowing "lens" pushed on top of the persistent filter. A lens NEVER mutates the
///     saved/base filter; it contributes exclude-of-complement <see cref="SavedFilter" /> criteria and/or a time-window
///     <see cref="DateFilter" /> that <see cref="EffectiveFilterBuilder" /> folds into the effective applied filter.
/// </summary>
public sealed record FilterLens
{
    public FilterLensId Id { get; init; } = FilterLensId.Create();

    public required string Label { get; init; }

    public required LensKind Kind { get; init; }

    public ImmutableList<SavedFilter> ExcludeFilters { get; init; } = [];

    public ImmutableList<SavedFilter> PromoteFilters { get; init; } = [];

    public DateFilter? Window { get; init; }

    public string? OriginLog { get; init; }
}
