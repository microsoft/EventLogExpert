// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Filter;

namespace EventLogExpert.UI.FilterGroup;

public sealed record ToggleFilterExcludedAction(FilterGroupId ParentId, FilterId Id);
