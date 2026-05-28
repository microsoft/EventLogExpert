// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Provider.Resolution;
using EventLogExpert.Runtime.Database.Upgrade;
using System.Collections.Immutable;

namespace EventLogExpert.Runtime.Database;

/// <summary>
///     Thin façade that composes the database sub-services (entry store, classification, upgrade, import, recovery)
///     and forwards every <see cref="IDatabaseService" /> member to the appropriate sub-service. Sub-services are
///     DI-managed singletons; disposal forwards to <see cref="DatabaseUpgradeService" /> (idempotent — safe for both
///     explicit disposal and DI container shutdown).
/// </summary>
internal sealed class DatabaseService(
    DatabaseRegistry registry,
    DatabaseClassificationService classificationService,
    DatabaseUpgradeService upgradeService,
    DatabaseImportService importService,
    DatabaseRecoveryService recoveryService) : IDatabaseService, IActiveDatabases
{
    public const string UpgradeBackupSuffix = DatabaseFileOperations.UpgradeBackupSuffix;

    private readonly DatabaseClassificationService _classificationService = classificationService;
    private readonly DatabaseImportService _importService = importService;
    private readonly DatabaseRecoveryService _recoveryService = recoveryService;
    private readonly DatabaseRegistry _registry = registry;
    private readonly DatabaseUpgradeService _upgradeService = upgradeService;

    public event EventHandler? EntriesChanged
    {
        add => _registry.EntriesChanged += value;
        remove => _registry.EntriesChanged -= value;
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

    public IReadOnlyList<DatabaseEntry> Entries => _registry.Entries;

    public Task InitialClassificationTask => _classificationService.InitialClassificationTask;

    public ImmutableList<string> Paths => _registry.ActiveDatabases;

    public int QueuedBatchCount => _upgradeService.QueuedBatchCount;

    public Task ClassifyEntriesAsync(CancellationToken cancellationToken = default) =>
        _classificationService.ClassifyEntriesAsync(cancellationToken);

    public Task<bool> DeleteEntryWithBackupAsync(string fileName, CancellationToken cancellationToken = default) =>
        _recoveryService.DeleteEntryWithBackupAsync(fileName, cancellationToken);

    public ValueTask DisposeAsync() => _upgradeService.DisposeAsync();

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

    public void MarkStatus(string fileName, DatabaseStatus status) => _registry.MarkStatus(fileName, status);

    public void Refresh() => _registry.Refresh();

    public Task RemoveAsync(
        string fileName,
        Func<CancellationToken, Task>? prepareForDeletionAsync = null,
        CancellationToken cancellationToken = default) =>
        _recoveryService.RemoveAsync(fileName, prepareForDeletionAsync, cancellationToken);

    public Task<bool> RestoreFromBackupAsync(string fileName, CancellationToken cancellationToken = default) =>
        _recoveryService.RestoreFromBackupAsync(fileName, cancellationToken);

    public Task RetryClassificationAsync(string fileName, CancellationToken cancellationToken = default) =>
        _classificationService.RetryClassificationAsync(fileName, cancellationToken);

    public void Toggle(string fileName) => _registry.Toggle(fileName);

    public Task<UpgradeBatchResult> UpgradeBatchAsync(
        IReadOnlyList<string> fileNames,
        UpgradeProgressScope scope,
        CancellationToken cancellationToken = default) =>
        _upgradeService.UpgradeBatchAsync(fileNames, scope, cancellationToken);
}
