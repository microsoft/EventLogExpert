// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.LogTable;

public sealed record RenameGroupAction(LogTabGroupId GroupId, string NewName);
