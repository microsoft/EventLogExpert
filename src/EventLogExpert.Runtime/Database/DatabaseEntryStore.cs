// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Databases;
using EventLogExpert.Eventing.Logging;
using EventLogExpert.Runtime.Common.Files;
using System.Collections.Immutable;

namespace EventLogExpert.Runtime.Database;

internal sealed class DatabaseEntryStore(
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

    public ImmutableList<DatabaseEntry> SnapshotEntries()
    {
        lock (_mutationLock)
        {
            return _entries;
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

    public void MarkStatus(string fileName, DatabaseStatus status)
    {
        lock (_mutationLock)
        {
            var index = FindEntryIndex(_entries, fileName);

            if (index < 0)
            {
                throw new InvalidOperationException(
                    $"{nameof(DatabaseEntryStore)}.{nameof(MarkStatus)}: no entry found with file name '{fileName}'.");
            }

            var current = _entries[index];

            if (current.Status == status) { return; }

            ImmutableList<DatabaseEntry> nextSnapshot = _entries.SetItem(index, current with { Status = status });
            _entries = nextSnapshot;
        }

        RaiseEntriesChanged();
    }

    public void Toggle(string fileName)
    {
        // Defense-in-depth: the UI disables toggle while a remove/import/upgrade is in
        // flight, but a stale click could still reach here. ReserveFileOperation throws
        // if another op holds the reservation; catch + log so the UI sees a no-op rather
        // than an unhandled exception.
        FileOperationReservation reservation;

        try
        {
            reservation = ReserveFileOperation(fileName, nameof(Toggle));
        }
        catch (InvalidOperationException ex)
        {
            SafeLog(() => _traceLogger.Trace(
                $"{nameof(DatabaseEntryStore)}.{nameof(Toggle)}: skipping toggle of '{fileName}' because another operation is in progress: {ex.Message}"));

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
                        $"{nameof(DatabaseEntryStore)}.{nameof(Toggle)}: no entry found with file name '{fileName}'.");
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

    public void RestoreEnabledIfChanged(string fileName, bool wasEnabled)
    {
        bool changed = false;

        // Re-find by fileName under the lock — the caller's snapshot is stale by now.
        // Only mutate the IsEnabled field on whatever entry matches; status / backup /
        // other fields may have been updated by classification or another path.
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

    public void ApplyClassificationResults(
        IReadOnlyDictionary<string, (DatabaseStatus Status, bool BackupExists)> statuses)
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

                if (entry.Status == newState.Status && entry.BackupExists == newState.BackupExists) { continue; }

                builder[i] = entry with { Status = newState.Status, BackupExists = newState.BackupExists };

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

    public void PersistDisabled(ImmutableList<DatabaseEntry> snapshot) =>
        _preferences.DisabledDatabasesPreference = snapshot
            .Where(entry => !entry.IsEnabled)
            .Select(entry => entry.FileName)
            .ToList();

    public DatabaseEntry LookupEntryOrThrow(string fileName, string callerName)
    {
        lock (_mutationLock)
        {
            var index = FindEntryIndex(_entries, fileName);

            if (index < 0)
            {
                throw new InvalidOperationException(
                    $"{nameof(DatabaseEntryStore)}.{callerName}: no entry found with file name '{fileName}'.");
            }

            return _entries[index];
        }
    }

    public FileOperationReservation ReserveFileOperation(string fileName, string callerName)
    {
        lock (_mutationLock)
        {
            if (!_filesInOperation.Add(fileName))
            {
                throw new InvalidOperationException(
                    $"{nameof(DatabaseEntryStore)}.{callerName}: cannot operate on '{fileName}' while another operation is in progress for the same database.");
            }
        }

        return new FileOperationReservation(this, fileName);
    }

    internal void ReleaseFileOperation(string fileName)
    {
        lock (_mutationLock)
        {
            _filesInOperation.Remove(fileName);
        }
    }

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
                    $"{nameof(DatabaseEntryStore)}.{nameof(RaiseEntriesChanged)}: subscriber threw: {ex}"));
            }
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
            _traceLogger.Warning($"{nameof(DatabaseEntryStore)}.{nameof(EnumerateDatabaseFileNames)} failed: {ex}");
            return [];
        }
    }

    internal static int FindEntryIndex(ImmutableList<DatabaseEntry> snapshot, string fileName) =>
        snapshot.FindIndex(entry => string.Equals(entry.FileName, fileName, StringComparison.OrdinalIgnoreCase));

    internal static bool IsActive(DatabaseEntry entry) => entry is { IsEnabled: true, Status: DatabaseStatus.Ready };

    internal static void SafeLog(Action log)
    {
        try { log(); }
        catch
        { /* Logger faults must not propagate from defensive logging sites. */
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
    private readonly DatabaseEntryStore _store;
    private readonly string _fileName;
    private bool _disposed;

    internal FileOperationReservation(DatabaseEntryStore store, string fileName)
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
