// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.LogTable;

internal sealed record RenameGroupAction(LogTabGroupId GroupId, string NewName);
