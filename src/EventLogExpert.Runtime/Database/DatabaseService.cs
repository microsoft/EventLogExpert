// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Databases;
using EventLogExpert.Eventing.Logging;
using EventLogExpert.Runtime.Common.Files;
using EventLogExpert.Runtime.Database.Upgrade;
using System.Collections.Immutable;

namespace EventLogExpert.Runtime.Database;

/// <summary>
///     Thin façade that composes the database sub-services (entry store, classification, upgrade, import, recovery)
///     and forwards every <see cref="IDatabaseService" /> member to the appropriate sub-service. Only the upgrade service
///     is disposable.
/// </summary>
internal sealed class DatabaseService : IDatabaseService, IActiveDatabasePathsProvider
{
    public const string UpgradeBackupSuffix = DatabaseFileOperations.UpgradeBackupSuffix;

    private readonly DatabaseClassificationService _classificationService;
    private readonly DatabaseEntryStore _entryStore;
    private readonly DatabaseImportService _importService;
    private readonly DatabaseRecoveryService _recoveryService;
    private readonly DatabaseUpgradeService _upgradeService;

    public DatabaseService(
        FileLocationOptions fileLocationOptions,
        IDatabasePreferencesProvider preferences,
        ITraceLogger traceLogger)
    {
        _entryStore = new DatabaseEntryStore(fileLocationOptions, preferences, traceLogger);
        _entryStore.Refresh();

        _classificationService = new DatabaseClassificationService(_entryStore, fileLocationOptions, traceLogger);

        _upgradeService = new DatabaseUpgradeService(
            _entryStore,
            _classificationService.InitialClassificationTask,
            traceLogger);

        _importService = new DatabaseImportService(
            _entryStore,
            _classificationService,
            _upgradeService,
            fileLocationOptions,
            traceLogger);

        _recoveryService = new DatabaseRecoveryService(
            _entryStore,
            _classificationService,
            fileLocationOptions,
            traceLogger);
    }

    public event EventHandler? EntriesChanged
    {
        add => _entryStore.EntriesChanged += value;
        remove => _entryStore.EntriesChanged -= value;
    }

    public event EventHandler<UpgradeBatchCompletedEventArgs>? UpgradeBatchCompleted
    {
        add => _upgradeService.UpgradeBatchCompleted += value;
        remove => _upgradeService.UpgradeBatchCompleted -= value;
    }

    public event EventHandler<UpgradeBatchProgressEventArgs>? UpgradeBatchProgress
    {
        add => _upgradeService.UpgradeBatchProgress += value;
        remove => _upgradeService.UpgradeBatchProgress -= value;
    }

    public event EventHandler<UpgradeBatchStartedEventArgs>? UpgradeBatchStarted
    {
        add => _upgradeService.UpgradeBatchStarted += value;
        remove => _upgradeService.UpgradeBatchStarted -= value;
    }

    public ImmutableList<string> ActiveDatabases => _entryStore.ActiveDatabases;

    public IReadOnlyList<DatabaseEntry> Entries => _entryStore.Entries;

    public Task InitialClassificationTask => _classificationService.InitialClassificationTask;

    public int QueuedBatchCount => _upgradeService.QueuedBatchCount;

    public Task ClassifyEntriesAsync(CancellationToken cancellationToken = default) =>
        _classificationService.ClassifyEntriesAsync(cancellationToken);

    public Task<bool> DeleteEntryWithBackupAsync(string fileName, CancellationToken cancellationToken = default) =>
        _recoveryService.DeleteEntryWithBackupAsync(fileName, cancellationToken);

    public Task<IReadOnlyList<string>> EnumerateZipDbEntryNamesAsync(
        string sourceZipPath,
        CancellationToken cancellationToken = default) =>
        _importService.EnumerateZipDbEntryNamesAsync(sourceZipPath, cancellationToken);

    public Task<ImportResult> ImportAsync(
        IEnumerable<string> sourceFilePaths,
        CancellationToken cancellationToken = default) =>
        _importService.ImportAsync(sourceFilePaths, cancellationToken);

    public Task<ImportResult> ImportAsync(
        IEnumerable<string> sourceFilePaths,
        IReadOnlySet<string> skipFileNames,
        CancellationToken cancellationToken = default) =>
        _importService.ImportAsync(sourceFilePaths, skipFileNames, cancellationToken);

    public void MarkStatus(string fileName, DatabaseStatus status) => _entryStore.MarkStatus(fileName, status);

    public void Refresh() => _entryStore.Refresh();

    public Task RemoveAsync(
        string fileName,
        Func<CancellationToken, Task>? prepareForDeletionAsync = null,
        CancellationToken cancellationToken = default) =>
        _recoveryService.RemoveAsync(fileName, prepareForDeletionAsync, cancellationToken);

    public Task<bool> RestoreFromBackupAsync(string fileName, CancellationToken cancellationToken = default) =>
        _recoveryService.RestoreFromBackupAsync(fileName, cancellationToken);

    public void Toggle(string fileName) => _entryStore.Toggle(fileName);

    public Task<UpgradeBatchResult> UpgradeBatchAsync(
        IReadOnlyList<string> fileNames,
        UpgradeProgressScope scope,
        CancellationToken cancellationToken = default) =>
        _upgradeService.UpgradeBatchAsync(fileNames, scope, cancellationToken);

    public ValueTask DisposeAsync() => _upgradeService.DisposeAsync();
}
