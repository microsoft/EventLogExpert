// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Persistence;

namespace EventLogExpert.UI.FilterGroup;

internal sealed record RemoveFilterAction(FilterGroupId ParentId, FilterId Id);
