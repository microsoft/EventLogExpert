// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Persistence;

namespace EventLogExpert.UI.FilterGroup;

public sealed record AddGroupAction(SavedFilterGroup? FilterGroup = null);
