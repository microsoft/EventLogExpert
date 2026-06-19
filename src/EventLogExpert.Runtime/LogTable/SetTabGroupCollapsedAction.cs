// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.LogTable;

public sealed record SetTabGroupCollapsedAction(LogTabGroupId GroupId, bool Collapsed);
