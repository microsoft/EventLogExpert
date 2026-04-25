// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.UI.Models;

/// <summary>
///     Mutable editor mirror of <see cref="BasicSubClause" /> used by the Basic-filter UI. Carries an
///     <see cref="Id" /> so Blazor can stably <c>@key</c> rendered rows and identify the row to remove.
/// </summary>
public sealed class BasicSubClauseDraft
{
    public BasicFilterCriteriaDraft Criteria { get; set; } = new();

    public FilterId Id { get; init; } = FilterId.Create();

    public bool JoinWithAny { get; set; }

    public BasicSubClause ToSubClause() => new(Criteria.ToCriteria(), JoinWithAny);
}
