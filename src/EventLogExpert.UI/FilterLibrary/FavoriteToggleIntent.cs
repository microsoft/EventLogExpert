// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.FilterLibrary;

namespace EventLogExpert.UI.FilterLibrary;

public sealed record FavoriteToggleIntent(LibraryEntryId EntryId, bool NewIsFavorite);
