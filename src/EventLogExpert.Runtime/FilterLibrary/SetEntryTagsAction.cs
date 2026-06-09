// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Collections.Immutable;

namespace EventLogExpert.Runtime.FilterLibrary;

internal sealed record SetEntryTagsAction(LibraryEntryId EntryId, ImmutableList<string> Tags);
