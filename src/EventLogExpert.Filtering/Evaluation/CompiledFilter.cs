// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.Eventing.Structured;

namespace EventLogExpert.Filtering.Evaluation;

/// <summary>
///     Immutable compiled artifact: the runnable predicate plus the cached XML-access flag used to decide whether
///     logs must be opened with pre-rendered XML.
/// </summary>
/// <param name="Predicate">
///     The bool predicate evaluated against each <see cref="ResolvedEvent" />; for a UserData filter
///     it is the tri-state <see cref="Evaluate" /> collapsed to bool (absent field and <see cref="FilterMatch.Unknown" />
///     read as non-match).
/// </param>
/// <param name="RequiresXml"><c>true</c> when the source expression references <see cref="ResolvedEvent.Xml" />.</param>
public sealed record CompiledFilter(Func<ResolvedEvent, bool> Predicate, bool RequiresXml)
{
    public Func<ResolvedEvent, FilterMatch>? Evaluate { get; init; }
}
