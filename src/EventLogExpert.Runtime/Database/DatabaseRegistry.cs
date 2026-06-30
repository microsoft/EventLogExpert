// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Provider.Resolution;
using EventLogExpert.Runtime.Common.Files;
using System.Collections.Immutable;

namespace EventLogExpert.Runtime.Database;

internal sealed class DatabaseRegistry(
    FileLocationOptions fileLocationOptions,
    IDatabasePreferencesProvider preferences,
    ITraceLogger traceLogger)
{
    private readonly FileLocationOptions _fileLocationOptions = fileLocationOptions;
    private readonly HashSet<string> _filesInOperation = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _mutationLock = new();
    private readonly IDatabasePreferencesProvider _preferences = preferences;
    private readonly ITraceLogger _traceLogger = traceLogger;

    private ImmutableList<DatabaseEntry> _entries = [];

    public event EventHandler? EntriesChanged;

    public ImmutableList<string> ActiveDatabases =>
        _entries
            .Where(IsActive)
            .Select(entry => entry.FullPath)
            .ToImmutableList();

    public IReadOnlyList<DatabaseEntry> Entries => _entries;

    public void ApplyClassificationResults(
        IReadOnlyDictionary<string, DatabaseClassificationResult> statuses)
    {
        if (statuses.Count == 0) { return; }

        var changed = false;

        lock (_mutationLock)
        {
            var builder = _entries.ToBuilder();

            for (var i = 0; i < builder.Count; i++)
            {
                var entry = builder[i];

                if (!statuses.TryGetValue(entry.FileName, out var newState)) { continue; }

                if (entry.Status == newState.Status
                    && entry.BackupExists == newState.BackupExists
                    && entry.OsStamps.SequenceEqual(newState.OsStamps))
                {
                    continue;
                }

                builder[i] = entry with
                {
                    Status = newState.Status,
                    BackupExists = newState.BackupExists,
                    OsStamps = newState.OsStamps,
                };

                changed = true;
            }

            if (changed)
            {
                _entries = builder.ToImmutable();
            }
        }

        if (changed)
        {
            RaiseEntriesChanged();
        }
    }

    public IEnumerable<string> EnumerateDatabaseFileNames()
    {
        if (!Directory.Exists(_fileLocationOptions.DatabasePath)) { return []; }

        try
        {
            return Directory
                .EnumerateFiles(_fileLocationOptions.DatabasePath, "*.db")
                .Select(Path.GetFileName)
                .Where(name => !string.IsNullOrEmpty(name))
                .Cast<string>()
                .ToList();
        }
        catch (Exception ex)
        {
            _traceLogger.Warning($"{nameof(DatabaseRegistry)}.{nameof(EnumerateDatabaseFileNames)} failed: {ex}");

            return [];
        }
    }

    public DatabaseEntry LookupEntryOrThrow(string fileName, string callerName)
    {
        lock (_mutationLock)
        {
            var index = FindEntryIndex(_entries, fileName);

            if (index < 0)
            {
                throw new InvalidOperationException(
                    $"{nameof(DatabaseRegistry)}.{callerName}: no entry found with file name '{fileName}'.");
            }

            return _entries[index];
        }
    }

    public void MarkFreshlyImportedDisabled(IReadOnlyCollection<string> freshlyImportedNames)
    {
        if (freshlyImportedNames.Count == 0) { return; }

        lock (_mutationLock)
        {
            var disabledSet = _preferences.DisabledDatabasesPreference
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var added = false;

            foreach (var name in freshlyImportedNames)
            {
                if (disabledSet.Add(name)) { added = true; }
            }

            if (added)
            {
                _preferences.DisabledDatabasesPreference = disabledSet.ToList();
            }
        }
    }

    public void MarkStatus(string fileName, DatabaseStatus status)
    {
        lock (_mutationLock)
        {
            var index = FindEntryIndex(_entries, fileName);

            if (index < 0)
            {
                throw new InvalidOperationException(
                    $"{nameof(DatabaseRegistry)}.{nameof(MarkStatus)}: no entry found with file name '{fileName}'.");
            }

            var current = _entries[index];

            if (current.Status == status) { return; }

            ImmutableList<DatabaseEntry> nextSnapshot = _entries.SetItem(index, current with { Status = status });
            _entries = nextSnapshot;
        }

        RaiseEntriesChanged();
    }

    public void PersistDisabled(ImmutableList<DatabaseEntry> snapshot) =>
        _preferences.DisabledDatabasesPreference = snapshot
            .Where(entry => !entry.IsEnabled)
            .Select(entry => entry.FileName)
            .ToList();

    public void RaiseEntriesChanged()
    {
        var handler = EntriesChanged;

        if (handler is null) { return; }

        foreach (var subscriber in handler.GetInvocationList())
        {
            try
            {
                ((EventHandler)subscriber).Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                SafeLog(() => _traceLogger.Warning(
                    $"{nameof(DatabaseRegistry)}.{nameof(RaiseEntriesChanged)}: subscriber threw: {ex}"));
            }
        }
    }

    public void Refresh()
    {
        lock (_mutationLock)
        {
            var disabled = _preferences.DisabledDatabasesPreference.ToHashSet(StringComparer.OrdinalIgnoreCase);

            var existingByFileName = _entries.ToDictionary(
                entry => entry.FileName,
                StringComparer.OrdinalIgnoreCase);

            var fileNames = EnumerateDatabaseFileNames();
            var sortedFileNames = DatabasePathSorter.Sort(fileNames);

            ImmutableList<DatabaseEntry> nextSnapshot = sortedFileNames
                .Select(fileName =>
                {
                    var isEnabled = !disabled.Contains(fileName);

                    return existingByFileName.TryGetValue(fileName, out var existing)
                        ? existing with { IsEnabled = isEnabled }
                        : new DatabaseEntry(
                            fileName,
                            Path.Join(_fileLocationOptions.DatabasePath, fileName),
                            isEnabled,
                            DatabaseStatus.NotClassified);
                })
                .ToImmutableList();

            _entries = nextSnapshot;
        }

        RaiseEntriesChanged();
    }

    public FileOperationReservation ReserveFileOperation(string fileName, string callerName)
    {
        lock (_mutationLock)
        {
            if (!_filesInOperation.Add(fileName))
            {
                throw new InvalidOperationException(
                    $"{nameof(DatabaseRegistry)}.{callerName}: cannot operate on '{fileName}' while another operation is in progress for the same database.");
            }
        }

        return new FileOperationReservation(this, fileName);
    }

    public void RestoreEnabledIfChanged(string fileName, bool wasEnabled)
    {
        bool changed = false;

        // Re-find under the lock; the caller snapshot may be stale, and other fields must be preserved.
        lock (_mutationLock)
        {
            var index = FindEntryIndex(_entries, fileName);

            if (index < 0) { return; }

            var current = _entries[index];

            if (current.IsEnabled == wasEnabled) { return; }

            _entries = _entries.SetItem(index, current with { IsEnabled = wasEnabled });
            PersistDisabled(_entries);
            changed = true;
        }

        if (changed) { RaiseEntriesChanged(); }
    }

    public (bool Found, bool WasEnabled) SetEnabled(string fileName, bool isEnabled, bool persist)
    {
        bool found;
        bool wasEnabled;
        bool changed;

        lock (_mutationLock)
        {
            var index = FindEntryIndex(_entries, fileName);

            if (index < 0)
            {
                return (false, false);
            }

            var current = _entries[index];
            found = true;
            wasEnabled = current.IsEnabled;
            changed = current.IsEnabled != isEnabled;

            if (changed)
            {
                _entries = _entries.SetItem(index, current with { IsEnabled = isEnabled });

                if (persist) { PersistDisabled(_entries); }
            }
        }

        if (changed) { RaiseEntriesChanged(); }

        return (found, wasEnabled);
    }

    public ImmutableList<DatabaseEntry> SnapshotEntries()
    {
        lock (_mutationLock)
        {
            return _entries;
        }
    }

    public void Toggle(string fileName)
    {
        // Stale UI clicks can race active file operations; reservation failure is a logged no-op.
        FileOperationReservation reservation;

        try
        {
            reservation = ReserveFileOperation(fileName, nameof(Toggle));
        }
        catch (InvalidOperationException ex)
        {
            SafeLog(() => _traceLogger.Trace(
                $"{nameof(DatabaseRegistry)}.{nameof(Toggle)}: skipping toggle of '{fileName}' because another operation is in progress: {ex.Message}"));

            return;
        }

        using (reservation)
        {
            lock (_mutationLock)
            {
                var index = FindEntryIndex(_entries, fileName);

                if (index < 0)
                {
                    throw new InvalidOperationException(
                        $"{nameof(DatabaseRegistry)}.{nameof(Toggle)}: no entry found with file name '{fileName}'.");
                }

                var current = _entries[index];
                var updated = current with { IsEnabled = !current.IsEnabled };
                ImmutableList<DatabaseEntry> nextSnapshot = _entries.SetItem(index, updated);

                PersistDisabled(nextSnapshot);
                _entries = nextSnapshot;
            }
        }

        RaiseEntriesChanged();
    }

    public bool TryRemoveAndPersist(string fileName)
    {
        bool removed;

        lock (_mutationLock)
        {
            var index = FindEntryIndex(_entries, fileName);
            ImmutableList<DatabaseEntry> nextSnapshot = index >= 0 ? _entries.RemoveAt(index) : _entries;

            PersistDisabled(nextSnapshot);

            removed = index >= 0;

            if (removed) { _entries = nextSnapshot; }
        }

        if (removed) { RaiseEntriesChanged(); }

        return removed;
    }

    public bool UpdateEntryStatusAndBackup(string fileName, DatabaseStatus status, bool backupExists)
    {
        var changed = false;

        lock (_mutationLock)
        {
            var index = FindEntryIndex(_entries, fileName);

            if (index < 0) { return false; }

            var current = _entries[index];

            if (current.Status == status && current.BackupExists == backupExists) { return true; }

            _entries = _entries.SetItem(index, current with { Status = status, BackupExists = backupExists });
            changed = true;
        }

        if (changed) { RaiseEntriesChanged(); }

        return true;
    }

    internal static int FindEntryIndex(ImmutableList<DatabaseEntry> snapshot, string fileName) =>
        snapshot.FindIndex(entry => string.Equals(entry.FileName, fileName, StringComparison.OrdinalIgnoreCase));

    internal static bool IsActive(DatabaseEntry entry) => entry is { IsEnabled: true, Status: DatabaseStatus.Ready };

    internal static void SafeLog(Action log)
    {
        try { log(); }
        catch
        { /* Defensive logging must not throw. */
        }
    }

    internal void ReleaseFileOperation(string fileName)
    {
        lock (_mutationLock)
        {
            _filesInOperation.Remove(fileName);
        }
    }

    internal void SafeRaise<TArgs>(EventHandler<TArgs>? handler, object sender, TArgs args, string eventName)
        where TArgs : EventArgs
    {
        if (handler is null) { return; }

        foreach (var subscriber in handler.GetInvocationList())
        {
            try
            {
                ((EventHandler<TArgs>)subscriber).Invoke(sender, args);
            }
            catch (Exception ex)
            {
                SafeLog(() => _traceLogger.Warning(
                    $"{sender.GetType().Name}.{eventName}: subscriber threw: {ex}"));
            }
        }
    }
}

internal struct FileOperationReservation : IDisposable
{
    private readonly DatabaseRegistry _store;
    private readonly string _fileName;
    private bool _disposed;

    internal FileOperationReservation(DatabaseRegistry store, string fileName)
    {
        _store = store;
        _fileName = fileName;
        _disposed = false;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _store.ReleaseFileOperation(_fileName);
    }
}
