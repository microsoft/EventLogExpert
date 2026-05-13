// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Events;

namespace EventLogExpert.Filtering;

/// <summary>
///     Immutable compiled artifact: the runnable predicate plus the cached XML-access flag used to decide whether
///     logs must be opened with pre-rendered XML.
/// </summary>
/// <param name="Predicate">The compiled lambda evaluated against each <see cref="ResolvedEvent" />.</param>
/// <param name="RequiresXml"><c>true</c> when the source expression references <see cref="ResolvedEvent.Xml" />.</param>
public sealed record CompiledFilter(Func<ResolvedEvent, bool> Predicate, bool RequiresXml);
