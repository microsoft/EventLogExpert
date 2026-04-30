// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Models;

namespace EventLogExpert.UI.Interfaces;

public interface IDatabaseService
{
    event EventHandler? EntriesChanged;

    IReadOnlyList<DatabaseEntry> Entries { get; }

    /// <summary>
    ///     Inspects each <see cref="DatabaseEntry" /> on disk (in read-only mode without auto-creating a schema) and
    ///     updates its <see cref="DatabaseEntry.Status" /> from the on-disk schema version. Per-entry classification
    ///     failures (e.g., file locked, file missing) leave that entry at its current status and continue with the rest.
    ///     Raises <see cref="EntriesChanged" /> exactly once when the pass completes.
    /// </summary>
    Task ClassifyEntriesAsync(CancellationToken cancellationToken = default);

    Task<ImportResult> ImportAsync(IEnumerable<string> sourceFilePaths, CancellationToken cancellationToken = default);

    void MarkStatus(string fileName, DatabaseStatus status);

    void Refresh();

    void Remove(string fileName);

    void Toggle(string fileName);
}
