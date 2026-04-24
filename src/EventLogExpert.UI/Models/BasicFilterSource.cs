// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Collections.Immutable;

namespace EventLogExpert.UI.Models;

/// <summary>
///     Immutable source representation of a Basic filter (the editable structure).
///     Compiled into a runtime predicate via <see cref="EventLogExpert.UI.Services.IFilterService.TryParse(BasicFilterSource, out string)" />
///     plus <see cref="EventLogExpert.UI.Services.FilterCompiler.TryCompile" />.
/// </summary>
public sealed record BasicFilterSource(
    BasicFilterCriteria Main,
    ImmutableList<BasicSubClause> SubClauses)
{
    public static BasicFilterSource Empty { get; } = new(new BasicFilterCriteria(), []);
}
