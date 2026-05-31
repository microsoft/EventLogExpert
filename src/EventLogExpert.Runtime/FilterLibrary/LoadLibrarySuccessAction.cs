// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Collections.Immutable;

namespace EventLogExpert.Runtime.FilterLibrary;

internal sealed record LoadLibrarySuccessAction(ImmutableList<LibraryEntry> Entries);
