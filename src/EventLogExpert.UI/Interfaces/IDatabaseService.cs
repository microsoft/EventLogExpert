// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Models;

namespace EventLogExpert.UI.Interfaces;

public interface IDatabaseService
{
    event EventHandler? EntriesChanged;

    IReadOnlyList<DatabaseEntry> Entries { get; }

    Task<ImportResult> ImportAsync(IEnumerable<string> sourceFilePaths, CancellationToken cancellationToken = default);

    void MarkStatus(string fileName, DatabaseStatus status);

    void Refresh();

    void Remove(string fileName);

    void Toggle(string fileName);
}
