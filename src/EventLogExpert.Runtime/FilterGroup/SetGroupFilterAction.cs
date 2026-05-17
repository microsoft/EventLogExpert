// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Persistence;

namespace EventLogExpert.Runtime.FilterGroup;

internal sealed record SetGroupFilterAction(FilterGroupId ParentId, SavedFilter Filter);
