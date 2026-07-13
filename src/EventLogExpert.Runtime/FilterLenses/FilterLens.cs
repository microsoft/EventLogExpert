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

    /// <summary>
    ///     Exclude criteria whose "hide everything else" arm is decisive (Match) for an absent field value, so the lens
    ///     narrows to exactly the intended rows without leaking absent-field rows. Only <see cref="FilterLensFactory" /> may
    ///     produce these, which is what enforces the total-operator invariant.
    /// </summary>
    public ImmutableList<SavedFilter> ExcludeFilters { get; init; } = [];

    public DateFilter? Window { get; init; }

    /// <summary>
    ///     The owning log the lens originated from - the source event's <c>OwningLog</c> (the app's internal log name,
    ///     which is also the name carried by a user-initiated log close). Used only for lifecycle: the lens is dropped when
    ///     that log is closed by the user. A null origin clears only on close-all.
    /// </summary>
    public string? OriginLog { get; init; }
}
