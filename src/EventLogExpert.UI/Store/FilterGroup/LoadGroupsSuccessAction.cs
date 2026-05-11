// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Filter;

namespace EventLogExpert.UI.Store.FilterGroup;

public sealed record LoadGroupsSuccessAction(IEnumerable<SavedFilterGroup> Groups);
