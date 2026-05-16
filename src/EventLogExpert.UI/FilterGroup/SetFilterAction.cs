// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Persistence;

namespace EventLogExpert.UI.FilterGroup;

internal sealed record SetFilterAction(FilterGroupId ParentId, SavedFilter Filter);
