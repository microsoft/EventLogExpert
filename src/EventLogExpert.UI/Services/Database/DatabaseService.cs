// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Databases;
using EventLogExpert.Eventing.Logging;
using EventLogExpert.Eventing.ProviderDatabase;
using EventLogExpert.UI.Common.Preferences;
using EventLogExpert.UI.Interfaces;
using EventLogExpert.UI.Models;
using EventLogExpert.UI.Options;
using Microsoft.Data.Sqlite;
using System.Collections.Immutable;
using System.IO.Compression;
using System.Threading.Channels;

namespace EventLogExpert.UI.Services;

public sealed class DatabaseService : IDatabaseService, IActiveDatabasePathsProvider
{
    public const string UpgradeBackupSuffix = ".upgrade.bak";

    private readonly Task _consumerTask;
    private readonly CancellationTokenSource _disposeCts = new();
    private readonly FileLocationOptions _fileLocationOptions;
    private readonly HashSet<string> _filesInOperation = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _mutationLock = new();
    private readonly IPreferencesProvider _preferences;
    private readonly ITraceLogger _traceLogger;
    private readonly Channel<UpgradeBatch> _upgradeQueue = Channel.CreateUnbounded<UpgradeBatch>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });

    private int _disposed;
    private ImmutableList<DatabaseEntry> _entries = [];
    private int _queuedBatchCount;

    public DatabaseService(
        FileLocationOptions fileLocationOptions,
        IPreferencesProvider preferences,
        ITraceLogger traceLogger)
    {
        _fileLocationOptions = fileLocationOptions;
        _preferences = preferences;
        _traceLogger = traceLogger;

        Refresh();

        InitialClassificationTask = StartInitialClassificationAsync();

        _consumerTask = Task.Run(ConsumeUpgradeQueueAsync);
    }

    public event EventHandler? EntriesChanged;

    public event EventHandler<UpgradeBatchCompletedEventArgs>? UpgradeBatchCompleted;

    public event EventHandler<UpgradeBatchProgressEventArgs>? UpgradeBatchProgress;

    public event EventHandler<UpgradeBatchStartedEventArgs>? UpgradeBatchStarted;

    public ImmutableList<string> ActiveDatabases =>
        _entries
            .Where(IsActive)
            .Select(entry => entry.FullPath)
            .ToImmutableList();

    public IReadOnlyList<DatabaseEntry> Entries => _entries;

    public Task InitialClassificationTask { get; }

    public int QueuedBatchCount => Volatile.Read(ref _queuedBatchCount);

    public async Task ClassifyEntriesAsync(CancellationToken cancellationToken = default)
    {
        ImmutableList<DatabaseEntry> snapshot;

        lock (_mutationLock)
        {
            snapshot = _entries;
        }

        if (snapshot.Count == 0) { return; }

        var statuses = await Task.Run(
            () =>
            {
                var perFile =
                    new Dictionary<string, (DatabaseStatus Status, bool BackupExists)>(StringComparer.OrdinalIgnoreCase);

                foreach (var entry in snapshot)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    try
                    {
                        if (!File.Exists(entry.FullPath) || new FileInfo(entry.FullPath).Length == 0)
                        {
                            perFile[entry.FileName] = (DatabaseStatus.UnrecognizedSchema, false);

                            continue;
                        }

                        using var context = new ProviderDbContext(
                            entry.FullPath,
                            readOnly: false,
                            ensureCreated: false,
                            logger: _traceLogger);

                        var state = context.IsUpgradeNeeded();
                        var status = MapSchemaVersionToStatus(state.CurrentVersion);
                        var backupExists = ProbeOrCleanupBackup(entry, status);

                        perFile[entry.FileName] = (status, backupExists);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        perFile[entry.FileName] = (DatabaseStatus.ClassificationFailed, false);

                        SafeLog(() => _traceLogger.Warning(
                            $"{nameof(DatabaseService)}.{nameof(ClassifyEntriesAsync)} failed to classify '{entry.FileName}': {ex}"));
                    }
                }

                return perFile;
            },
            cancellationToken).ConfigureAwait(false);

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

    public async Task<bool> DeleteEntryWithBackupAsync(string fileName, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var reservation = ReserveFileOperation(fileName, nameof(DeleteEntryWithBackupAsync));

        var entry = LookupEntryOrThrow(fileName, nameof(DeleteEntryWithBackupAsync));

        var success = await Task.Run(() => DeleteFilesCore(entry)).ConfigureAwait(false);

        if (!success) { return false; }

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

        return true;
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) { return; }

        _upgradeQueue.Writer.TryComplete();

        try
        {
            await _disposeCts.CancelAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            SafeLog(() => _traceLogger.Warning(
                $"{nameof(DatabaseService)}.{nameof(DisposeAsync)}: cancellation source faulted: {ex}"));
        }

        try
        {
            await _consumerTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected on disposal.
        }
        catch (Exception ex)
        {
            SafeLog(() => _traceLogger.Warning(
                $"{nameof(DatabaseService)}.{nameof(DisposeAsync)}: consumer task faulted: {ex}"));
        }

        _disposeCts.Dispose();
    }

    public async Task<IReadOnlyList<string>> EnumerateZipDbEntryNamesAsync(
        string sourceZipPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(sourceZipPath);

        ZipArchive archive;

        try
        {
            archive = await ZipFile.OpenReadAsync(sourceZipPath, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _traceLogger.Warning(
                $"{nameof(DatabaseService)}.{nameof(EnumerateZipDbEntryNamesAsync)} failed to open '{sourceZipPath}': {ex}");

            return [];
        }

        await using (archive)
        {
            var names = new List<string>();

            foreach (var entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (string.IsNullOrEmpty(entry.Name)) { continue; }

                if (!Path.GetExtension(entry.Name).Equals(".db", StringComparison.OrdinalIgnoreCase)) { continue; }

                names.Add(entry.Name);
            }

            return names;
        }
    }

    public async Task<ImportResult> ImportAsync(
        IEnumerable<string> sourceFilePaths,
        CancellationToken cancellationToken = default) =>
        await ImportAsync(sourceFilePaths, ImmutableHashSet<string>.Empty, cancellationToken)
            .ConfigureAwait(false);

    public async Task<ImportResult> ImportAsync(
        IEnumerable<string> sourceFilePaths,
        IReadOnlySet<string> skipFileNames,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourceFilePaths);
        ArgumentNullException.ThrowIfNull(skipFileNames);

        var skipSet = skipFileNames as HashSet<string> is { Comparer: var comparer } &&
            comparer.Equals(StringComparer.OrdinalIgnoreCase)
                ? skipFileNames
                : skipFileNames.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var importedCount = 0;
        var failures = new List<ImportFailure>();
        var importedNames = new List<string>();
        var freshlyImportedNames = new List<string>();

        Directory.CreateDirectory(_fileLocationOptions.DatabasePath);

        var existingFileNames = _entries
            .Select(entry => entry.FileName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var sourceFilePath in sourceFilePaths)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrEmpty(sourceFilePath)) { continue; }

            var fileName = Path.GetFileName(sourceFilePath);

            if (string.IsNullOrEmpty(fileName)) { continue; }

            if (Path.GetExtension(fileName).Equals(".zip", StringComparison.OrdinalIgnoreCase))
            {
                var (zipImported, zipFailures, zipImportedNames) = await ImportZipAsync(
                        sourceFilePath,
                        fileName,
                        skipSet,
                        cancellationToken)
                    .ConfigureAwait(false);

                importedCount += zipImported;
                failures.AddRange(zipFailures);

                foreach (var name in zipImportedNames)
                {
                    importedNames.Add(name);

                    if (!existingFileNames.Contains(name)) { freshlyImportedNames.Add(name); }
                }
            }
            else
            {
                if (skipSet.Contains(fileName)) { continue; }

                try
                {
                    var destinationPath = Path.Join(_fileLocationOptions.DatabasePath, fileName);

                    using (ReserveFileOperation(fileName, nameof(ImportAsync)))
                    {
                        File.Copy(sourceFilePath, destinationPath, true);
                    }

                    importedCount++;
                    importedNames.Add(fileName);

                    if (!existingFileNames.Contains(fileName)) { freshlyImportedNames.Add(fileName); }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    failures.Add(new ImportFailure(fileName, ex.Message));

                    _traceLogger.Warning(
                        $"{nameof(DatabaseService)}.{nameof(ImportAsync)} failed to copy '{fileName}': {ex}");
                }
            }
        }

        if (importedCount <= 0)
        {
            return new ImportResult(importedCount, failures, []);
        }

        if (freshlyImportedNames.Count > 0)
        {
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

        Refresh();

        try
        {
            await ClassifyEntriesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _traceLogger.Warning(
                $"{nameof(DatabaseService)}.{nameof(ImportAsync)} post-import classification failed: {ex}");
        }

        IReadOnlyList<ImportFailure> upgradeFailures = [];

        var importedSet = importedNames.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var upgradeNeeded = _entries
            .Where(entry => importedSet.Contains(entry.FileName) && entry.Status == DatabaseStatus.UpgradeRequired)
            .Select(entry => entry.FileName)
            .ToList();

        if (upgradeNeeded.Count <= 0)
        {
            return new ImportResult(importedCount, failures, upgradeFailures);
        }

        var batchResult = await UpgradeBatchAsync(
                upgradeNeeded,
                UpgradeProgressScope.Background,
                cancellationToken)
            .ConfigureAwait(false);

        upgradeFailures = batchResult.Failed
            .Select(failure => new ImportFailure(failure.FileName, failure.Message))
            .ToList();

        return new ImportResult(importedCount, failures, upgradeFailures);
    }

    public void MarkStatus(string fileName, DatabaseStatus status)
    {
        lock (_mutationLock)
        {
            var index = FindEntryIndex(_entries, fileName);

            if (index < 0)
            {
                throw new InvalidOperationException(
                    $"{nameof(DatabaseService)}.{nameof(MarkStatus)}: no entry found with file name '{fileName}'.");
            }

            var current = _entries[index];

            if (current.Status == status) { return; }

            ImmutableList<DatabaseEntry> nextSnapshot = _entries.SetItem(index, current with { Status = status });
            _entries = nextSnapshot;
        }

        RaiseEntriesChanged();
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

    public async Task RemoveAsync(
        string fileName,
        Func<CancellationToken, Task>? prepareForDeletionAsync = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var reservation = ReserveFileOperation(fileName, nameof(RemoveAsync));

        // Wait for initial classification so callers don't race with the background scan
        // (which can mutate IsEnabled / Status / BackupExists for this entry mid-flight).
        try
        {
            await InitialClassificationTask.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            SafeLog(() => _traceLogger.Trace(
                $"{nameof(DatabaseService)}.{nameof(RemoveAsync)}: InitialClassificationTask faulted unexpectedly: {ex}"));
        }

        cancellationToken.ThrowIfCancellationRequested();

        // Phase 1: disable the entry under the mutation lock so EventResolver can no longer
        // pick this database up on its next construction. Capture wasEnabled so we can
        // restore on rollback if a later phase fails. RaiseEntriesChanged outside the lock.
        bool wasEnabled;
        bool entryFound;

        lock (_mutationLock)
        {
            var index = FindEntryIndex(_entries, fileName);

            if (index < 0)
            {
                throw new InvalidOperationException(
                    $"{nameof(DatabaseService)}.{nameof(RemoveAsync)}: no entry found with file name '{fileName}'.");
            }

            var current = _entries[index];
            wasEnabled = current.IsEnabled;
            entryFound = true;

            if (wasEnabled)
            {
                _entries = _entries.SetItem(index, current with { IsEnabled = false });
                PersistDisabled(_entries);
            }
        }

        if (entryFound && wasEnabled) { RaiseEntriesChanged(); }

        // Phase 2: let the caller close any open log views before we touch the file.
        // Always invoked when a callback is supplied — a previously-disabled entry can still
        // be referenced by IEventResolver instances constructed before the disable (e.g., the
        // user disabled this DB and declined the modal-close reload prompt). Those resolvers
        // hold DbContexts whose connections are NOT in the pool until the per-event service
        // scope disposes, so ClearAllPools below cannot release them and File.Delete races.
        if (prepareForDeletionAsync is not null)
        {
            try
            {
                await prepareForDeletionAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                SafeLog(() => _traceLogger.Warning(
                    $"{nameof(DatabaseService)}.{nameof(RemoveAsync)}: prepare-for-deletion callback failed: {ex}"));

                RestoreEnabledIfChanged(fileName, wasEnabled);

                throw;
            }
        }

        // Phase 3: clear the SQLite connection pool and delete the files. Pool clear is
        // required so the OS file handles backing the pooled connections are closed; the
        // closed-but-pooled connections still hold the file handle without
        // FILE_SHARE_DELETE on Windows.
        try
        {
            SqliteConnection.ClearAllPools();

            DeleteDatabaseFiles(fileName);
        }
        catch (Exception ex)
        {
            SafeLog(() => _traceLogger.Warning(
                $"{nameof(DatabaseService)}.{nameof(RemoveAsync)}: deleting files for '{fileName}' failed: {ex}"));

            RestoreEnabledIfChanged(fileName, wasEnabled);

            throw;
        }

        // Phase 4: drop the entry from the snapshot. Re-find by fileName under the lock —
        // the snapshot reference captured in Phase 1 is stale because Phase 1 mutated it.
        bool removed = false;

        lock (_mutationLock)
        {
            var index = FindEntryIndex(_entries, fileName);

            if (index >= 0)
            {
                _entries = _entries.RemoveAt(index);
                PersistDisabled(_entries);
                removed = true;
            }
        }

        if (removed) { RaiseEntriesChanged(); }
    }

    public async Task<bool> RestoreFromBackupAsync(string fileName, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var reservation = ReserveFileOperation(fileName, nameof(RestoreFromBackupAsync));

        var entry = LookupEntryOrThrow(fileName, nameof(RestoreFromBackupAsync));

        var success = await Task.Run(() => RestoreFilesCore(entry)).ConfigureAwait(false);

        if (!success) { return false; }

        try
        {
            await ClassifyEntriesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            SafeLog(() => _traceLogger.Warning(
                $"{nameof(DatabaseService)}.{nameof(RestoreFromBackupAsync)} post-restore classification failed: {ex}"));
        }

        return true;
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
                $"{nameof(DatabaseService)}.{nameof(Toggle)}: skipping toggle of '{fileName}' because another operation is in progress: {ex.Message}"));

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
                        $"{nameof(DatabaseService)}.{nameof(Toggle)}: no entry found with file name '{fileName}'.");
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

    public async Task<UpgradeBatchResult> UpgradeBatchAsync(
        IReadOnlyList<string> fileNames,
        UpgradeProgressScope scope,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fileNames);
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        var dedupedNames = DedupePreservingOrder(fileNames);

        if (cancellationToken.IsCancellationRequested)
        {
            return new UpgradeBatchResult([], dedupedNames, []);
        }

        try
        {
            await InitialClassificationTask.ConfigureAwait(false);
        }
        catch
        {
            // InitialClassificationTask is contractually never-faulting; defensive try/catch only.
        }

        var acceptable = new List<DatabaseEntry>(dedupedNames.Count);
        var rejected = new List<UpgradeFailure>();

        lock (_mutationLock)
        {
            var snapshot = _entries;
            var byName = new Dictionary<string, DatabaseEntry>(snapshot.Count, StringComparer.OrdinalIgnoreCase);

            foreach (var entry in snapshot)
            {
                byName[entry.FileName] = entry;
            }

            foreach (var fileName in dedupedNames)
            {
                if (!byName.TryGetValue(fileName, out var entry))
                {
                    rejected.Add(new UpgradeFailure(fileName, "Entry not found"));
                    continue;
                }

                if (entry.BackupExists)
                {
                    rejected.Add(new UpgradeFailure(
                        fileName,
                        "Recovery required — resolve via Settings or recovery prompt first"));

                    continue;
                }

                if (entry.Status is not (DatabaseStatus.UpgradeRequired or DatabaseStatus.UpgradeFailed))
                {
                    rejected.Add(new UpgradeFailure(
                        fileName,
                        $"Cannot upgrade entry in status '{entry.Status}'"));

                    continue;
                }

                acceptable.Add(entry);
            }
        }

        if (acceptable.Count == 0)
        {
            return new UpgradeBatchResult([], [], rejected);
        }

        var batchCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _disposeCts.Token);

        var tcs = new TaskCompletionSource<UpgradeBatchResult>(TaskCreationOptions.RunContinuationsAsynchronously);

        var batch = new UpgradeBatch(
            Guid.NewGuid(),
            scope,
            acceptable,
            tcs,
            batchCts,
            rejected);

        Interlocked.Increment(ref _queuedBatchCount);

        if (_upgradeQueue.Writer.TryWrite(batch))
        {
            return await tcs.Task.ConfigureAwait(false);
        }

        Interlocked.Decrement(ref _queuedBatchCount);
        batchCts.Dispose();

        return new UpgradeBatchResult([], acceptable.Select(entry => entry.FileName).ToArray(), rejected);
    }

    private static IReadOnlyList<string> DedupePreservingOrder(IReadOnlyList<string> source)
    {
        var seen = new HashSet<string>(source.Count, StringComparer.OrdinalIgnoreCase);
        var deduped = new List<string>(source.Count);

        foreach (var item in source)
        {
            if (seen.Add(item))
            {
                deduped.Add(item);
            }
        }

        return deduped;
    }

    private static int FindEntryIndex(ImmutableList<DatabaseEntry> snapshot, string fileName) =>
        snapshot.FindIndex(entry => string.Equals(entry.FileName, fileName, StringComparison.OrdinalIgnoreCase));

    private static bool IsActive(DatabaseEntry entry) => entry is { IsEnabled: true, Status: DatabaseStatus.Ready };

    private static DatabaseStatus MapSchemaVersionToStatus(int currentVersion) =>
        currentVersion switch
        {
            ProviderDatabaseSchemaVersion.Current => DatabaseStatus.Ready,
            3 => DatabaseStatus.UpgradeRequired,
            1 or 2 => DatabaseStatus.ObsoleteSchema,
            _ => DatabaseStatus.UnrecognizedSchema,
        };

    private static void SafeLog(Action log)
    {
        try { log(); }
        catch { /* Logger faults must not propagate from defensive logging sites. */ }
    }

    private static void WalCheckpoint(string dbPath)
    {
        using (var connection = new SqliteConnection($"Data Source={dbPath}"))
        {
            connection.Open();

            using var cmd = connection.CreateCommand();

            cmd.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
            cmd.ExecuteNonQuery();
        }

        SqliteConnection.ClearAllPools();
    }

    private async Task ConsumeUpgradeQueueAsync()
    {
        try
        {
            await foreach (var batch in _upgradeQueue.Reader.ReadAllAsync(_disposeCts.Token).ConfigureAwait(false))
            {
                await ProcessBatchAsync(batch).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on dispose.
        }
        catch (Exception ex)
        {
            SafeLog(() => _traceLogger.Warning(
                $"{nameof(DatabaseService)}.{nameof(ConsumeUpgradeQueueAsync)}: consumer faulted: {ex}"));
        }
        finally
        {
            DrainPendingBatches();
        }
    }

    private void DeleteDatabaseFiles(string fileName)
    {
        var basePath = Path.Combine(_fileLocationOptions.DatabasePath, fileName);

        File.Delete(basePath + "-journal");
        File.Delete(basePath + "-wal");
        File.Delete(basePath + "-shm");
        File.Delete(basePath + UpgradeBackupSuffix);
        File.Delete(basePath);
    }

    private bool DeleteFilesCore(DatabaseEntry entry)
    {
        var mainPath = entry.FullPath;

        if (!TryDeleteFile(mainPath + "-journal", nameof(DeleteEntryWithBackupAsync))) { return false; }

        if (!TryDeleteFile(mainPath + "-wal", nameof(DeleteEntryWithBackupAsync))) { return false; }

        if (!TryDeleteFile(mainPath + "-shm", nameof(DeleteEntryWithBackupAsync))) { return false; }

        return TryDeleteFile(mainPath + UpgradeBackupSuffix, nameof(DeleteEntryWithBackupAsync)) &&
            TryDeleteFile(mainPath, nameof(DeleteEntryWithBackupAsync));
    }

    private void DrainPendingBatches()
    {
        while (_upgradeQueue.Reader.TryRead(out var batch))
        {
            Interlocked.Decrement(ref _queuedBatchCount);

            try { batch.BatchCts.Cancel(); }
            catch (Exception) { /* CTS may already be disposed; cancellation moot. */ }

            try { batch.BatchCts.Dispose(); }
            catch (Exception) { /* Already disposed; ignore. */ }

            batch.Tcs.TrySetException(
                new OperationCanceledException("DatabaseService disposed before batch processed."));
        }
    }

    private IEnumerable<string> EnumerateDatabaseFileNames()
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
            _traceLogger.Warning($"{nameof(DatabaseService)}.{nameof(EnumerateDatabaseFileNames)} failed: {ex}");
            return [];
        }
    }

    private async Task<(int Imported, IReadOnlyList<ImportFailure> Failures, IReadOnlyList<string> ImportedNames)>
        ImportZipAsync(
            string sourceZipPath,
            string zipFileName,
            IReadOnlySet<string> skipFileNames,
            CancellationToken cancellationToken)
    {
        var imported = 0;
        var failures = new List<ImportFailure>();
        var importedNames = new List<string>();

        ZipArchive archive;

        try
        {
            archive = await ZipFile.OpenReadAsync(sourceZipPath, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            failures.Add(new ImportFailure(zipFileName, $"Could not open archive: {ex.Message}"));

            _traceLogger.Warning(
                $"{nameof(DatabaseService)}.{nameof(ImportZipAsync)} failed to open '{zipFileName}': {ex}");

            return (imported, failures, importedNames);
        }

        await using (archive)
        {
            foreach (var entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (string.IsNullOrEmpty(entry.Name)) { continue; }

                if (!Path.GetExtension(entry.Name).Equals(".db", StringComparison.OrdinalIgnoreCase)) { continue; }

                if (skipFileNames.Contains(entry.Name)) { continue; }

                var destinationPath = Path.Join(_fileLocationOptions.DatabasePath, entry.Name);

                try
                {
                    using (ReserveFileOperation(entry.Name, nameof(ImportZipAsync)))
                    {
                        await using (var entryStream = await entry.OpenAsync(cancellationToken))
                        {
                            await using (var fileStream = File.Create(destinationPath))
                            {
                                await entryStream.CopyToAsync(fileStream, cancellationToken).ConfigureAwait(false);
                            }
                        }
                    }

                    imported++;
                    importedNames.Add(entry.Name);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    failures.Add(new ImportFailure(entry.Name, ex.Message));

                    _traceLogger.Warning(
                        $"{nameof(DatabaseService)}.{nameof(ImportZipAsync)} failed to extract '{entry.Name}' from '{zipFileName}': {ex}");

                    try
                    {
                        if (File.Exists(destinationPath))
                        {
                            File.Delete(destinationPath);
                        }
                    }
                    catch (Exception cleanupEx)
                    {
                        _traceLogger.Warning(
                            $"{nameof(DatabaseService)}.{nameof(ImportZipAsync)} failed to clean up partial extract '{destinationPath}': {cleanupEx}");
                    }
                }
            }
        }

        return (imported, failures, importedNames);
    }

    private DatabaseEntry LookupEntryOrThrow(string fileName, string callerName)
    {
        lock (_mutationLock)
        {
            var index = FindEntryIndex(_entries, fileName);

            if (index < 0)
            {
                throw new InvalidOperationException(
                    $"{nameof(DatabaseService)}.{callerName}: no entry found with file name '{fileName}'.");
            }

            return _entries[index];
        }
    }

    private void PersistDisabled(ImmutableList<DatabaseEntry> snapshot) =>
        _preferences.DisabledDatabasesPreference = snapshot
            .Where(entry => !entry.IsEnabled)
            .Select(entry => entry.FileName)
            .ToList();

    private bool ProbeOrCleanupBackup(DatabaseEntry entry, DatabaseStatus status)
    {
        var backupPath = entry.FullPath + UpgradeBackupSuffix;

        if (status == DatabaseStatus.UpgradeRequired)
        {
            try
            {
                return File.Exists(backupPath);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                SafeLog(() => _traceLogger.Warning(
                    $"{nameof(DatabaseService)}.{nameof(ProbeOrCleanupBackup)} probe failed for '{entry.FileName}': {ex}"));

                return true;
            }
        }

        if (status == DatabaseStatus.Ready)
        {
            try
            {
                File.Delete(backupPath);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                SafeLog(() => _traceLogger.Warning(
                    $"{nameof(DatabaseService)}.{nameof(ProbeOrCleanupBackup)} stale .upgrade.bak cleanup failed for '{entry.FileName}': {ex}"));
            }
        }

        return false;
    }

    private async Task ProcessBatchAsync(UpgradeBatch batch)
    {
        Interlocked.Decrement(ref _queuedBatchCount);

        var succeeded = new List<string>(batch.Entries.Count);
        var cancelled = new List<string>(batch.Entries.Count);
        var failed = new List<UpgradeFailure>(batch.AlreadyRejected.Count + batch.Entries.Count);
        failed.AddRange(batch.AlreadyRejected);
        var wasCancelled = false;

        try
        {
            SafeRaise(
                UpgradeBatchStarted,
                new UpgradeBatchStartedEventArgs(batch.Id, batch.Scope, batch.Entries.Count, batch.BatchCts),
                nameof(UpgradeBatchStarted));

            for (var i = 0; i < batch.Entries.Count; i++)
            {
                var entry = batch.Entries[i];

                if (batch.BatchCts.Token.IsCancellationRequested)
                {
                    wasCancelled = true;

                    for (var j = i; j < batch.Entries.Count; j++)
                    {
                        cancelled.Add(batch.Entries[j].FileName);
                    }

                    break;
                }

                try
                {
                    await UpgradeAsync(entry.FileName, i + 1, batch.Id, batch.BatchCts.Token).ConfigureAwait(false);

                    succeeded.Add(entry.FileName);
                }
                catch (OperationCanceledException) when (batch.BatchCts.Token.IsCancellationRequested)
                {
                    wasCancelled = true;
                    cancelled.Add(entry.FileName);

                    for (var j = i + 1; j < batch.Entries.Count; j++)
                    {
                        cancelled.Add(batch.Entries[j].FileName);
                    }

                    break;
                }
                catch (UpgradeRollbackFailedException ex)
                {
                    failed.Add(new UpgradeFailure(entry.FileName, ex.Message));

                    if (!batch.BatchCts.Token.IsCancellationRequested)
                    {
                        continue;
                    }

                    wasCancelled = true;

                    for (var j = i + 1; j < batch.Entries.Count; j++)
                    {
                        cancelled.Add(batch.Entries[j].FileName);
                    }

                    break;
                }
                catch (Exception ex)
                {
                    var message = ex is DatabaseUpgradeException dbEx ? dbEx.Reason : ex.Message;
                    failed.Add(new UpgradeFailure(entry.FileName, message));

                    SafeLog(() => _traceLogger.Warning(
                        $"{nameof(DatabaseService)}.{nameof(ProcessBatchAsync)}: '{entry.FileName}' upgrade threw: {ex}"));
                }
            }

            var result = new UpgradeBatchResult(succeeded, cancelled, failed);

            SafeRaise(
                UpgradeBatchCompleted,
                new UpgradeBatchCompletedEventArgs(batch.Id, result, wasCancelled),
                nameof(UpgradeBatchCompleted));

            batch.Tcs.TrySetResult(result);
        }
        catch (Exception ex)
        {
            SafeLog(() => _traceLogger.Warning(
                $"{nameof(DatabaseService)}.{nameof(ProcessBatchAsync)}: unhandled batch error: {ex}"));

            batch.Tcs.TrySetException(ex);
        }
        finally
        {
            try { batch.BatchCts.Dispose(); }
            catch (Exception) { /* Already disposed; ignore. */ }
        }
    }

    private void RaiseEntriesChanged()
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
                    $"{nameof(DatabaseService)}.{nameof(RaiseEntriesChanged)}: subscriber threw: {ex}"));
            }
        }
    }

    private void ReleaseFileOperation(string fileName)
    {
        lock (_mutationLock)
        {
            _filesInOperation.Remove(fileName);
        }
    }

    private FileOperationReservation ReserveFileOperation(string fileName, string callerName)
    {
        lock (_mutationLock)
        {
            if (!_filesInOperation.Add(fileName))
            {
                throw new InvalidOperationException(
                    $"{nameof(DatabaseService)}.{callerName}: cannot operate on '{fileName}' while another operation is in progress for the same database.");
            }
        }

        return new FileOperationReservation(this, fileName);
    }

    private void RestoreEnabledIfChanged(string fileName, bool wasEnabled)
    {
        bool changed = false;

        // Re-find by fileName under the lock — the Phase 1 snapshot is stale by now.
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

    private bool RestoreFilesCore(DatabaseEntry entry)
    {
        var mainPath = entry.FullPath;
        var backupPath = mainPath + UpgradeBackupSuffix;

        if (!File.Exists(backupPath))
        {
            SafeLog(() => _traceLogger.Warning(
                $"{nameof(DatabaseService)}.{nameof(RestoreFromBackupAsync)}: '{backupPath}' missing; nothing to restore."));

            return false;
        }

        if (!TryDeleteFile(mainPath + "-journal", nameof(RestoreFromBackupAsync))) { return false; }

        if (!TryDeleteFile(mainPath + "-wal", nameof(RestoreFromBackupAsync))) { return false; }

        if (!TryDeleteFile(mainPath + "-shm", nameof(RestoreFromBackupAsync))) { return false; }

        try
        {
            File.Copy(backupPath, mainPath, true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            SafeLog(() => _traceLogger.Warning(
                $"{nameof(DatabaseService)}.{nameof(RestoreFromBackupAsync)}: copy from '{backupPath}' to '{mainPath}' failed: {ex}"));

            return false;
        }

        return TryDeleteFile(backupPath, nameof(RestoreFromBackupAsync));
    }

    private void SafeRaise<TArgs>(EventHandler<TArgs>? handler, TArgs args, string eventName)
        where TArgs : EventArgs
    {
        if (handler is null) { return; }

        foreach (var subscriber in handler.GetInvocationList())
        {
            try
            {
                ((EventHandler<TArgs>)subscriber).Invoke(this, args);
            }
            catch (Exception ex)
            {
                SafeLog(() => _traceLogger.Warning(
                    $"{nameof(DatabaseService)}.{eventName}: subscriber threw: {ex}"));
            }
        }
    }

    private async Task StartInitialClassificationAsync()
    {
        try
        {
            await ClassifyEntriesAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            SafeLog(() => _traceLogger.Warning(
                $"{nameof(DatabaseService)}.{nameof(StartInitialClassificationAsync)}: initial classification failed: {ex}"));
        }
    }

    private bool TryDeleteFile(string path, string callerName)
    {
        try
        {
            File.Delete(path);

            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            SafeLog(() =>
                _traceLogger.Warning($"{nameof(DatabaseService)}.{callerName}: delete failed for '{path}': {ex}"));

            return false;
        }
    }

    private bool UpdateEntryStatusAndBackup(string fileName, DatabaseStatus status, bool backupExists)
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

    private async Task UpgradeAsync(string fileName, int position, Guid batchId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var reservation = ReserveFileOperation(fileName, nameof(UpgradeAsync));

        var entry = LookupEntryOrThrow(fileName, nameof(UpgradeAsync));

        if (entry.Status is not (DatabaseStatus.UpgradeRequired or DatabaseStatus.UpgradeFailed))
        {
            throw new InvalidOperationException($"Cannot upgrade entry in status '{entry.Status}'");
        }

        if (entry.BackupExists)
        {
            throw new InvalidOperationException("Recovery required — backup exists");
        }

        var backupPath = entry.FullPath + UpgradeBackupSuffix;

        if (File.Exists(backupPath))
        {
            UpdateEntryStatusAndBackup(fileName, entry.Status, true);

            throw new InvalidOperationException("Recovery required — .upgrade.bak already present");
        }

        await Task.Run(
            () =>
            {
                var backupCreated = false;
                var migrationStarted = false;
                var migrationCompleted = false;

                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    WalCheckpoint(entry.FullPath);

                    cancellationToken.ThrowIfCancellationRequested();

                    SafeRaise(
                        UpgradeBatchProgress,
                        new UpgradeBatchProgressEventArgs(batchId, position, fileName, UpgradePhase.BackingUp),
                        nameof(UpgradeBatchProgress));

                    try
                    {
                        File.Copy(entry.FullPath, backupPath, false);
                    }
                    catch (IOException ex) when (File.Exists(backupPath))
                    {
                        UpdateEntryStatusAndBackup(fileName, entry.Status, true);

                        throw new InvalidOperationException(
                            "Recovery required — .upgrade.bak appeared during backup",
                            ex);
                    }

                    backupCreated = true;

                    cancellationToken.ThrowIfCancellationRequested();

                    SafeRaise(
                        UpgradeBatchProgress,
                        new UpgradeBatchProgressEventArgs(batchId, position, fileName, UpgradePhase.MigratingSchema),
                        nameof(UpgradeBatchProgress));

                    migrationStarted = true;

                    using (var context = new ProviderDbContext(
                        entry.FullPath,
                        readOnly: false,
                        ensureCreated: false,
                        logger: _traceLogger))
                    {
                        context.PerformUpgradeIfNeeded();
                    }

                    migrationCompleted = true;
                    SqliteConnection.ClearAllPools();

                    SafeRaise(
                        UpgradeBatchProgress,
                        new UpgradeBatchProgressEventArgs(batchId, position, fileName, UpgradePhase.Verifying),
                        nameof(UpgradeBatchProgress));

                    if (!VerifyEntryReady(entry.FullPath))
                    {
                        throw new InvalidOperationException("Upgrade verification failed");
                    }

                    if (!TryDeleteFile(backupPath, nameof(UpgradeAsync)))
                    {
                        UpdateEntryStatusAndBackup(fileName, DatabaseStatus.Ready, true);

                        throw new UpgradeCleanupFailedException(
                            "Upgrade succeeded but backup cleanup failed; .upgrade.bak remains on disk");
                    }

                    UpdateEntryStatusAndBackup(fileName, DatabaseStatus.Ready, false);
                }
                catch (UpgradeCleanupFailedException)
                {
                    throw;
                }
                catch (OperationCanceledException)
                {
                    if (!backupCreated || migrationCompleted) { throw; }

                    if (RestoreFilesCore(entry))
                    {
                        UpdateEntryStatusAndBackup(fileName, DatabaseStatus.UpgradeRequired, false);
                    }
                    else
                    {
                        UpdateEntryStatusAndBackup(fileName, DatabaseStatus.UpgradeFailed, true);

                        throw new UpgradeRollbackFailedException(
                            $"Cancellation rollback failed for '{fileName}'; .upgrade.bak remains on disk");
                    }

                    throw;
                }
                catch (Exception ex)
                {
                    if (migrationStarted && !migrationCompleted)
                    {
                        if (!RestoreFilesCore(entry))
                        {
                            UpdateEntryStatusAndBackup(fileName, DatabaseStatus.UpgradeFailed, true);

                            throw new UpgradeRollbackFailedException(
                                $"Migration failed and rollback also failed for '{fileName}': {(ex is DatabaseUpgradeException dbEx ? dbEx.Reason : ex.Message)}");
                        }

                        UpdateEntryStatusAndBackup(fileName, DatabaseStatus.UpgradeFailed, false);
                    }
                    else if (migrationCompleted)
                    {
                        if (!RestoreFilesCore(entry))
                        {
                            UpdateEntryStatusAndBackup(fileName, DatabaseStatus.UpgradeFailed, true);

                            throw new UpgradeRollbackFailedException(
                                $"Verification or cleanup failed and rollback also failed for '{fileName}'");
                        }

                        UpdateEntryStatusAndBackup(fileName, DatabaseStatus.UpgradeFailed, false);
                    }
                    else if (backupCreated)
                    {
                        TryDeleteFile(backupPath, nameof(UpgradeAsync));
                    }

                    throw;
                }
            },
            cancellationToken).ConfigureAwait(false);
    }

    private bool VerifyEntryReady(string fullPath)
    {
        try
        {
            using var context = new ProviderDbContext(
                fullPath,
                readOnly: true,
                ensureCreated: false,
                logger: _traceLogger);

            return context.IsUpgradeNeeded().CurrentVersion == ProviderDatabaseSchemaVersion.Current;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            SafeLog(() => _traceLogger.Warning(
                $"{nameof(DatabaseService)}.{nameof(VerifyEntryReady)}: '{fullPath}' verification threw: {ex}"));

            return false;
        }
    }

    private struct FileOperationReservation : IDisposable
    {
        private readonly DatabaseService _service;
        private readonly string _fileName;
        private bool _disposed;

        internal FileOperationReservation(DatabaseService service, string fileName)
        {
            _service = service;
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
            _service.ReleaseFileOperation(_fileName);
        }
    }

    private sealed class UpgradeCleanupFailedException(string message) : Exception(message);

    private sealed class UpgradeRollbackFailedException(string message) : Exception(message);

    private sealed record UpgradeBatch(
        Guid Id,
        UpgradeProgressScope Scope,
        IReadOnlyList<DatabaseEntry> Entries,
        TaskCompletionSource<UpgradeBatchResult> Tcs,
        CancellationTokenSource BatchCts,
        IReadOnlyList<UpgradeFailure> AlreadyRejected);
}
