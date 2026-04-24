// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Models;

namespace EventLogExpert.UI.Models;

/// <summary>
///     Immutable compiled artifact: the runnable predicate plus the cached XML-access flag
///     used to decide whether logs must be opened with pre-rendered XML.
/// </summary>
/// <param name="Predicate">The compiled lambda evaluated against each <see cref="DisplayEventModel" />.</param>
/// <param name="RequiresXml">
///     <c>true</c> when the source expression references <see cref="DisplayEventModel.Xml" />.
/// </param>
public sealed record CompiledFilter(Func<DisplayEventModel, bool> Predicate, bool RequiresXml);
