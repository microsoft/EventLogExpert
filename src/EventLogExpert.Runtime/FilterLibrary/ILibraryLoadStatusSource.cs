// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.Common.Sources;

namespace EventLogExpert.Runtime.FilterLibrary;

public readonly record struct LibraryLoadStatus(bool IsLoaded, bool LoadError);

public interface ILibraryLoadStatusSource : IChangeNotifier
{
    LibraryLoadStatus Current { get; }
}
