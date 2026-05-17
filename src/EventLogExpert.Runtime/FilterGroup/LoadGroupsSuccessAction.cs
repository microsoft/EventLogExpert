// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Persistence;
using System.Collections.Immutable;

namespace EventLogExpert.Runtime.FilterGroup;

internal sealed record LoadGroupsSuccessAction(ImmutableList<SavedFilterGroup> Groups);
