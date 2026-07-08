// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Structured;

namespace EventLogExpert.Filtering.Emit;

/// <summary>
///     Three-valued And/Or combine over an ordered set of tri-state <see cref="FilterMatch" /> parts, shared by the
///     row <see cref="Emitter" /> UserData path and the column <see cref="ColumnEmitter" />. Generic over the part
///     invocation (a state plus a per-index evaluator) so the short-circuit is preserved: And returns on the first
///     <see cref="FilterMatch.NoMatch" /> and Or on the first <see cref="FilterMatch.Match" /> without invoking the
///     remaining parts (which may drive expensive UserData / Xml reads). The state is passed by value so a caller can
///     supply a closure-free context and allocate nothing per event.
/// </summary>
internal static class FilterMatchCombiner
{
    public static FilterMatch And<TState>(TState state, int count, Func<TState, int, FilterMatch> evaluatePart)
    {
        var result = FilterMatch.Match;

        for (var index = 0; index < count; index++)
        {
            var match = evaluatePart(state, index);

            if (match == FilterMatch.NoMatch) { return FilterMatch.NoMatch; }

            if (match == FilterMatch.Unknown) { result = FilterMatch.Unknown; }
        }

        return result;
    }

    public static FilterMatch Or<TState>(TState state, int count, Func<TState, int, FilterMatch> evaluatePart)
    {
        var result = FilterMatch.NoMatch;

        for (var index = 0; index < count; index++)
        {
            var match = evaluatePart(state, index);

            if (match == FilterMatch.Match) { return FilterMatch.Match; }

            if (match == FilterMatch.Unknown) { result = FilterMatch.Unknown; }
        }

        return result;
    }
}
