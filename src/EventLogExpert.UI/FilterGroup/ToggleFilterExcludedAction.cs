// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Persistence;

namespace EventLogExpert.UI.FilterGroup;

internal sealed record ToggleFilterExcludedAction(FilterGroupId ParentId, FilterId Id);
