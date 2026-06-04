// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.FilterLibrary;

public interface IFilterLibraryExportService
{
    ImportPreflight Deserialize(string json, IReadOnlyList<LibraryEntry> existingEntries);

    string Serialize(IReadOnlyList<LibraryEntry> entries);
}
