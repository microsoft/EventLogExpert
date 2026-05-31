// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Persistence;

namespace EventLogExpert.Runtime.FilterGroup;

internal sealed record SetGroupFilterExcludedAction(FilterGroupId ParentId, FilterId Id, bool IsExcluded);
