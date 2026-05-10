// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Models;

namespace EventLogExpert.UI.Store.FilterGroup;

public sealed record RemoveFilterAction(FilterGroupId ParentId, FilterId Id);
