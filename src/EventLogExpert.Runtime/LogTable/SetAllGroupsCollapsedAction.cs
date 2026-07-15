// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.LogTable;

// Public so LogTablePane can subscribe via SubscribeToAction, like the other LogTable actions.
public sealed record SetAllGroupsCollapsedAction(bool Collapsed);
