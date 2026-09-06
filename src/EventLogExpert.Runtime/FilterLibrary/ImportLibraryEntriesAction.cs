// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Collections.Immutable;

namespace EventLogExpert.Runtime.FilterLibrary;

internal sealed record ImportLibraryEntriesAction(ImmutableList<LibraryEntry> ToAdd, ImmutableList<LibraryEntry> ToUpdate, ImportSummary Summary);

public sealed record ImportSummary(int Added, int Replaced, int UpdatedTags, int Skipped, int Ambiguous);
