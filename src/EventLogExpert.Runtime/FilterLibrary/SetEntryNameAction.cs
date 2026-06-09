// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.FilterLibrary;

internal sealed record SetEntryNameAction(LibraryEntryId EntryId, string Name);
