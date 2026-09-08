// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Evaluation;
using EventLogExpert.Filtering.Persistence;
using System.Collections.Immutable;

namespace EventLogExpert.Runtime.FilterLenses;

internal sealed record CommitPromotedLensesAction(ImmutableList<PromotedLensCommit> Commits);

internal sealed record PromotedLensCommit(
    FilterLensId Id,
    ImmutableList<SavedFilter> Filters,
    DateFilter? Window);
