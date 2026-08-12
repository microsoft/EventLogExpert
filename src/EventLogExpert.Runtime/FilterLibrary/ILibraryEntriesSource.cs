// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.Common.Sources;
using System.Collections.Immutable;

namespace EventLogExpert.Runtime.FilterLibrary;

public interface ILibraryEntriesSource : IChangeNotifier
{
    ImmutableList<LibraryEntry> Current { get; }
}
