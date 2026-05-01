// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.EventProviderDatabase;
using EventLogExpert.Eventing.EventResolvers;
using EventLogExpert.Eventing.Helpers;
using EventLogExpert.UI.Interfaces;
using EventLogExpert.UI.Models;
using EventLogExpert.UI.Options;
using System.Collections.Immutable;
using System.IO.Compression;
using System.Text.RegularExpressions;

namespace EventLogExpert.UI.Services;

public sealed partial class DatabaseService : IDatabaseService, IDatabaseCollectionProvider
{
    public const string UpgradeBackupSuffix = ".upgrade.bak";

    private readonly FileLocationOptions _fileLocationOptions;
    private readonly Lock _mutationLock = new();
    private readonly IPreferencesProvider _preferences;
    private readonly ITraceLogger _traceLogger;

    private ImmutableList<DatabaseEntry> _entries = [];

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
    }

    public event EventHandler? EntriesChanged;

    public ImmutableList<string> ActiveDatabases =>
        _entries
            .Where(IsActive)
            .Select(entry => entry.FullPath)
            .ToImmutableList();

    public IReadOnlyList<DatabaseEntry> Entries => _entries;

    public Task InitialClassificationTask { get; }

    public async Task ClassifyEntriesAsync(CancellationToken cancellationToken = default)
    {
        // Snapshot under the lock so concurrent Refresh/Toggle/Remove calls cannot trigger an
        // index-out-of-range during the IO pass below.
        ImmutableList<DatabaseEntry> snapshot;

        lock (_mutationLock)
        {
            snapshot = _entries;
        }

        if (snapshot.Count == 0) { return; }

        // Whole pass runs on a worker thread because IsUpgradeNeeded performs synchronous SQLite IO
        // (PRAGMA queries via blocking ADO.NET). Driving it from the UI thread would block render.
        var statuses = await Task.Run(
            () =>
            {
                var perFile = new Dictionary<string, (DatabaseStatus Status, bool BackupExists)>(StringComparer.OrdinalIgnoreCase);

                foreach (var entry in snapshot)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    try
                    {
                        // Empty/missing files would otherwise classify as Ready (IsUpgradeNeeded sees
                        // no columns → currentVersion=Current → no upgrade needed). EventResolver
                        // would then call EnsureCreated on first access and silently rewrite the
                        // file with a V4 schema. Force these into UnrecognizedSchema so they are
                        // quarantined and surfaced in Settings instead of being mutated invisibly.
                        if (!File.Exists(entry.FullPath) || new FileInfo(entry.FullPath).Length == 0)
                        {
                            perFile[entry.FileName] = (DatabaseStatus.UnrecognizedSchema, false);
                            continue;
                        }

                        // ensureCreated:false so a missing/empty file is NOT auto-populated with the V4 schema
                        // during inspection — that would silently mask the very thing classification is for.
                        using var context = new EventProviderDbContext(
                            entry.FullPath,
                            readOnly: false,
                            ensureCreated: false,
                            _traceLogger);

                        var state = context.IsUpgradeNeeded();
                        var status = MapSchemaVersionToStatus(state.CurrentVersion);
                        var backupExists = ProbeOrCleanupBackup(entry, status);

                        perFile[entry.FileName] = (status, backupExists);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        perFile[entry.FileName] = (DatabaseStatus.ClassificationFailed, false);

                        SafeLog(() => _traceLogger.Warn(
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
            // Re-resolve indexes against the current snapshot — Refresh/Remove may have run while
            // classification was on the worker thread.
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

    public async Task<ImportResult> ImportAsync(
        IEnumerable<string> sourceFilePaths,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourceFilePaths);

        var importedCount = 0;
        var failures = new List<ImportFailure>();
        Directory.CreateDirectory(_fileLocationOptions.DatabasePath);

        foreach (var sourceFilePath in sourceFilePaths)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrEmpty(sourceFilePath)) { continue; }

            var fileName = Path.GetFileName(sourceFilePath);

            if (string.IsNullOrEmpty(fileName)) { continue; }

            if (Path.GetExtension(fileName).Equals(".zip", StringComparison.OrdinalIgnoreCase))
            {
                var (zipImported, zipFailures) = await ImportZipAsync(sourceFilePath, fileName, cancellationToken)
                    .ConfigureAwait(false);

                importedCount += zipImported;
                failures.AddRange(zipFailures);
            }
            else
            {
                try
                {
                    var destinationPath = Path.Join(_fileLocationOptions.DatabasePath, fileName);
                    File.Copy(sourceFilePath, destinationPath, true);
                    importedCount++;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    failures.Add(new ImportFailure(fileName, ex.Message));

                    _traceLogger.Warn(
                        $"{nameof(DatabaseService)}.{nameof(ImportAsync)} failed to copy '{fileName}': {ex}");
                }
            }
        }

        if (importedCount <= 0)
        {
            return new ImportResult(importedCount, failures);
        }

        Refresh();

        // Classify so freshly-imported V3 databases surface in settings as UpgradeRequired
        // (with the "restart required to upgrade" UX) rather than appearing as Ready.
        try
        {
            await ClassifyEntriesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _traceLogger.Warn(
                $"{nameof(DatabaseService)}.{nameof(ImportAsync)} post-import classification failed: {ex}");
        }

        return new ImportResult(importedCount, failures);
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
            var sortedFileNames = SortDatabases(fileNames);

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

    public void Remove(string fileName)
    {
        lock (_mutationLock)
        {
            var index = FindEntryIndex(_entries, fileName);

            if (index < 0)
            {
                throw new InvalidOperationException(
                    $"{nameof(DatabaseService)}.{nameof(Remove)}: no entry found with file name '{fileName}'.");
            }

            DeleteDatabaseFiles(fileName);

            ImmutableList<DatabaseEntry> nextSnapshot = _entries.RemoveAt(index);
            PersistDisabled(nextSnapshot);
            _entries = nextSnapshot;
        }

        RaiseEntriesChanged();
    }

    public void Toggle(string fileName)
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

        RaiseEntriesChanged();
    }

    private static int FindEntryIndex(ImmutableList<DatabaseEntry> snapshot, string fileName) =>
        snapshot.FindIndex(entry => string.Equals(entry.FileName, fileName, StringComparison.OrdinalIgnoreCase));

    private static bool IsActive(DatabaseEntry entry) => entry.IsEnabled && entry.Status == DatabaseStatus.Ready;

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
        catch { /* Ignore */}
    }

    private static IEnumerable<string> SortDatabases(IEnumerable<string> databases)
    {
        if (!databases.Any()) { return []; }

        var splitter = SplitFileName();

        return databases
            .Select(name =>
            {
                // Strip the .db extension before applying the version regex so that "Server 20.db"
                // splits into ("Server ", "20") instead of ("Server ", "20.db") — otherwise the
                // version capture never parses as a number and numeric sort falls back to
                // lexicographic ("Server 2.db" < "Server 20.db" but "Server 20.db" > "Server 10.db").
                var nameWithoutExt = Path.GetFileNameWithoutExtension(name);
                var match = splitter.Match(nameWithoutExt);

                if (match.Success)
                {
                    var versionString = match.Groups[2].Value;

                    // Try to parse the version as a number for proper numeric ordering.
                    // This ensures "10" sorts after "2" rather than before it (lexicographic).
                    int? numericVersion = int.TryParse(versionString, out var parsed) ? parsed : null;

                    return new
                    {
                        OriginalName = name,
                        FirstPart = match.Groups[1].Value + " ",
                        SecondPart = versionString,
                        NumericVersion = numericVersion
                    };
                }

                return new
                {
                    OriginalName = name,
                    FirstPart = nameWithoutExt,
                    SecondPart = "",
                    NumericVersion = (int?)null
                };
            })
            .OrderBy(name => name.FirstPart)
            .ThenByDescending(name => name.NumericVersion ?? int.MinValue)
            .ThenByDescending(name => name.SecondPart)
            .Select(name => name.OriginalName);
    }

    [GeneratedRegex("^(.+) (\\S+)$")]
    private static partial Regex SplitFileName();

    private void DeleteDatabaseFiles(string fileName)
    {
        // Delete the .db together with its SQLite sidecars (.db-wal, .db-shm) so a re-import
        // doesn't pick up stale write-ahead state.
        var directory = new DirectoryInfo(_fileLocationOptions.DatabasePath);

        if (!directory.Exists) { return; }

        foreach (var file in directory.GetFiles($"{fileName}*"))
        {
            file.Delete();
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
            _traceLogger.Warn($"{nameof(DatabaseService)}.{nameof(EnumerateDatabaseFileNames)} failed: {ex}");
            return [];
        }
    }

    private async Task<(int Imported, IReadOnlyList<ImportFailure> Failures)> ImportZipAsync(
        string sourceZipPath,
        string zipFileName,
        CancellationToken cancellationToken)
    {
        var imported = 0;
        var failures = new List<ImportFailure>();

        ZipArchive archive;

        try
        {
            archive = await ZipFile.OpenReadAsync(sourceZipPath, cancellationToken);
        }
        catch (Exception ex)
        {
            failures.Add(new ImportFailure(zipFileName, $"Could not open archive: {ex.Message}"));

            _traceLogger.Warn(
                $"{nameof(DatabaseService)}.{nameof(ImportZipAsync)} failed to open '{zipFileName}': {ex}");

            return (imported, failures);
        }

        await using (archive)
        {
            foreach (var entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (string.IsNullOrEmpty(entry.Name)) { continue; }

                if (!Path.GetExtension(entry.Name).Equals(".db", StringComparison.OrdinalIgnoreCase)) { continue; }

                var destinationPath = Path.Join(_fileLocationOptions.DatabasePath, entry.Name);

                try
                {
                    await using (var entryStream = await entry.OpenAsync(cancellationToken))
                    {
                        await using (var fileStream = File.Create(destinationPath))
                        {
                            await entryStream.CopyToAsync(fileStream, cancellationToken).ConfigureAwait(false);
                        }
                    }

                    imported++;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    failures.Add(new ImportFailure(entry.Name, ex.Message));

                    _traceLogger.Warn(
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
                        _traceLogger.Warn(
                            $"{nameof(DatabaseService)}.{nameof(ImportZipAsync)} failed to clean up partial extract '{destinationPath}': {cleanupEx}");
                    }
                }
            }
        }

        return (imported, failures);
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
                // Conservative on probe failure: surface a possibly-spurious recovery prompt
                // rather than silently denying the user a chance to restore from .upgrade.bak.
                SafeLog(() => _traceLogger.Warn(
                    $"{nameof(DatabaseService)}.{nameof(ProbeOrCleanupBackup)} probe failed for '{entry.FileName}': {ex}"));
                return true;
            }
        }

        if (status == DatabaseStatus.Ready)
        {
            // V4 is the canonical post-upgrade state; an existing .upgrade.bak is stale
            // (we crashed between rename and cleanup). File.Delete is a no-op if absent.
            try
            {
                File.Delete(backupPath);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                SafeLog(() => _traceLogger.Warn(
                    $"{nameof(DatabaseService)}.{nameof(ProbeOrCleanupBackup)} stale .upgrade.bak cleanup failed for '{entry.FileName}': {ex}"));
            }
        }

        return false;
    }

    private void RaiseEntriesChanged() => EntriesChanged?.Invoke(this, EventArgs.Empty);

    private async Task StartInitialClassificationAsync()
    {
        try
        {
            await ClassifyEntriesAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Honor InitialClassificationTask never-fault contract; absorbs subscriber throws.
            SafeLog(() => _traceLogger.Warn(
                $"{nameof(DatabaseService)}.{nameof(StartInitialClassificationAsync)}: initial classification failed: {ex}"));
        }
    }
}
