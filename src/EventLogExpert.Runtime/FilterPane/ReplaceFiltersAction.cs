// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Persistence;
using System.Collections.Immutable;

namespace EventLogExpert.Runtime.FilterPane;

internal sealed record ReplaceFiltersAction(ImmutableList<SavedFilter> Filters);
