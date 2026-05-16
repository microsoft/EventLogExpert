// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Persistence;
using EventLogExpert.UI.Filter;
using System.Collections.Immutable;

namespace EventLogExpert.UI.FilterPane;

/// <summary>
///     Host-facing seam for the FilterPane highlight selection and change-key hashing used by the log table's per-row
///     highlight pipeline. Stateless; safe as a singleton.
/// </summary>
public interface IHighlightSelector
{
    /// <summary>
    ///     Probabilistic change-key over the highlight-eligible subset of <paramref name="filters" />: enabled,
    ///     non-excluded, compiled, with an enum-defined <see cref="HighlightColor" />. Two filter lists that produce identical
    ///     highlight behavior return the same key; any mutation that changes the eligible set, order, colors, or any filter's
    ///     <c>Compiled</c> reference returns a different key. The 32-bit hash means collisions are theoretically possible; the
    ///     worst-case fallout is a stale highlight cache until the next non-colliding change.
    /// </summary>
    int ComputeHighlightKey(ImmutableList<SavedFilter> filters);

    /// <summary>
    ///     Returns the enabled, non-excluded, compiled, enum-defined-color subset of <paramref name="filters" /> in their
    ///     original order. The resulting array is the input to the table's first-match-wins highlight loop.
    /// </summary>
    SavedFilter[] Select(ImmutableList<SavedFilter> filters);
}
