// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.UI.Models;

/// <summary>
///     A single sub-criterion of a Basic filter, paired with how it joins to the previous clause.
/// </summary>
/// <param name="Criteria">The sub-criterion.</param>
/// <param name="JoinWithAny">
///     <c>true</c> ↔ OR with the previous clause; <c>false</c> ↔ AND.
///     Mirrors today's per-subfilter <c>ShouldCompareAny</c>.
/// </param>
public sealed record BasicSubClause(BasicFilterCriteria Criteria, bool JoinWithAny);
