// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Eventing.Structured;

namespace EventLogExpert.Filtering.Evaluation;

/// <summary>
///     Immutable compiled artifact for the column-direct filter backend: a single unified tri-state evaluator over
///     <see cref="IEventColumnReader" /> plus the cached XML-access flag. Unlike <see cref="CompiledFilter" /> there is no
///     bool <c>Predicate</c> twin - every arm (not just UserData) returns a <see cref="FilterMatch" />, so the
///     non-UserData arms return only <see cref="FilterMatch.Match" /> / <see cref="FilterMatch.NoMatch" /> and the
///     UserData arms carry the full tri-state.
/// </summary>
/// <param name="Evaluate">
///     Reads the event addressed by an <see cref="EventLocator" /> column-direct from the reader and
///     returns its tri-state match.
/// </param>
/// <param name="RequiresXml"><c>true</c> when the source expression references the event's XML.</param>
internal sealed record ColumnCompiledFilter(
    Func<IEventColumnReader, EventLocator, FilterMatch> Evaluate,
    bool RequiresXml);
