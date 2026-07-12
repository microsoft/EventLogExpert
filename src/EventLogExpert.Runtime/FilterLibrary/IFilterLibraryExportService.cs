// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.FilterLibrary;

public interface IFilterLibraryExportService
{
    ImportPreflight Deserialize(string json, IReadOnlyList<LibraryEntry> existingEntries);

    /// <summary>
    ///     Deserializes an import file, optionally normalizing Basic filters that contain an empty Contains/NotContains
    ///     value. When <paramref name="normalizeEmptyValues" /> is true, each such value is stripped; any filter left with an
    ///     empty criterion is removed and reported in <see cref="ImportPreflight.NormalizeRemovedFilterNames" />. When false,
    ///     affected entries are reported in <see cref="ImportPreflight.NormalizableEmptyValueEntryNames" /> and imported as
    ///     authored.
    /// </summary>
    ImportPreflight Deserialize(
        string json,
        IReadOnlyList<LibraryEntry> existingEntries,
        bool normalizeEmptyValues);

    string Serialize(IReadOnlyList<LibraryEntry> entries);
}
