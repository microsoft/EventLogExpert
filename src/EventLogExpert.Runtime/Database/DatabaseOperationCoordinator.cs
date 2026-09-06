// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.Banner;
using EventLogExpert.Runtime.Common.Files;
using EventLogExpert.Runtime.Database.Upgrade;
using EventLogExpert.Runtime.EventLog;

namespace EventLogExpert.Runtime.Database;

internal sealed class DatabaseOperationCoordinator(
    IDatabaseService databases,
    IInfoBannerService infoBanners,
    IErrorBannerService errorBanners,
    IFilePickerService filePicker,
    ILogReloadCoordinator logReload,
    ITraceLogger logger) : IDatabaseOperationCoordinator
{
    private readonly IDatabaseService _databases = databases;
    private readonly IErrorBannerService _errorBanners = errorBanners;
    private readonly IFilePickerService _filePicker = filePicker;
    private readonly IInfoBannerService _infoBanners = infoBanners;
    private readonly ILogReloadCoordinator _logReload = logReload;
    private readonly ITraceLogger _logger = logger;
    private readonly Lock _upgradeGate = new();
    private readonly HashSet<string> _upgradesInFlight = new(StringComparer.OrdinalIgnoreCase);

    public event Action? UpgradeStateChanged;

    public bool IsAnyUpgradeInFlight
    {
        get { using (_upgradeGate.EnterScope()) { return _upgradesInFlight.Count > 0; } }
    }

    public async Task ApplyPendingTogglesAsync(
        IReadOnlyCollection<string> fileNames,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fileNames);
        cancellationToken.ThrowIfCancellationRequested();

        foreach (var fileName in fileNames)
        {
            cancellationToken.ThrowIfCancellationRequested();

            await RunOperationAsync(
                operationName: "toggle",
                operation: new DatabaseOperation.Toggle(fileName),
                body: () =>
                {
                    _databases.Toggle(fileName);

                    return Task.CompletedTask;
                });
        }
    }

    public async Task<ImportOutcome> ImportAsync(
        Func<string, CancellationToken, Task<bool>>? askOverwriteAsync = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return await RunOperationAsync(
            operationName: "import",
            operation: new DatabaseOperation.Import(),
            fallbackOutcome: ImportOutcome.None,
            body: async () =>
            {
                var sourcePaths = await _filePicker.PickMultipleAsync(
                    "Please select database files to import",
                    FilePickerFileTypes.Database);

                if (sourcePaths.Count == 0) { return ImportOutcome.None; }

                return await ImportPathsCoreAsync(sourcePaths, enableOnImport: false, askOverwriteAsync, cancellationToken);
            });
    }

    public async Task<ImportOutcome> ImportPathsAsync(
        IReadOnlyList<string> sourcePaths,
        bool enableOnImport,
        Func<string, CancellationToken, Task<bool>>? askOverwriteAsync = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourcePaths);
        cancellationToken.ThrowIfCancellationRequested();

        if (sourcePaths.Count == 0) { return ImportOutcome.None; }

        return await RunOperationAsync(
            operationName: "import",
            operation: new DatabaseOperation.Import(),
            fallbackOutcome: ImportOutcome.None,
            body: () => ImportPathsCoreAsync(sourcePaths, enableOnImport, askOverwriteAsync, cancellationToken));
    }

    public bool IsUpgradeInFlight(string fileName)
    {
        ArgumentNullException.ThrowIfNull(fileName);

        using (_upgradeGate.EnterScope()) { return _upgradesInFlight.Contains(fileName); }
    }

    public async Task<RemoveOutcome> RemoveDatabaseAsync(
        string fileName,
        Func<bool, CancellationToken, Task<bool>> confirmRemoveAsync,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        ArgumentNullException.ThrowIfNull(confirmRemoveAsync);

        cancellationToken.ThrowIfCancellationRequested();

        var entry = _databases.Entries.FirstOrDefault(e =>
            string.Equals(e.FileName, fileName, StringComparison.OrdinalIgnoreCase));

        if (entry is null) { return RemoveOutcome.NotFound; }

        var isReadyAndEnabled = entry is { IsEnabled: true, Status: DatabaseStatus.Ready };
        var showCloseReopenWarning = isReadyAndEnabled && _logReload.HasActiveLogs;

        bool confirmed;

        try
        {
            confirmed = await confirmRemoveAsync(showCloseReopenWarning, cancellationToken);
        }
        catch (OperationCanceledException) { return RemoveOutcome.NotConfirmed; }
        catch (Exception ex)
        {
            _logger.Warning($"{nameof(RemoveDatabaseAsync)} confirm callback threw: {ex}");

            return RemoveOutcome.NotConfirmed;
        }

        if (!confirmed) { return RemoveOutcome.NotConfirmed; }

        // Bespoke flow preserves snapshot reopen semantics if removal fails after closing logs.
        var snapshot = new LogReopenSnapshot();
        bool removed = false;

        try
        {
            await _databases.RemoveAsync(
                fileName,
                ct => _logReload.PrepareForDatabaseRemovalAsync(snapshot, ct),
                cancellationToken);

            removed = true;
        }
        catch (OperationCanceledException)
        {
            // Cancellation is silent.
        }
        catch (Exception ex)
        {
            _logger.Warning($"{nameof(RemoveDatabaseAsync)} remove failed: {ex}");
            _errorBanners.ReportError(new DatabaseRemoveFailed(fileName, ex.Message));
        }

        bool logsReopened = false;

        if (snapshot.Items.Count > 0)
        {
            _logReload.ReopenAfterDatabaseRemoval(snapshot.Items);
            logsReopened = true;
        }

        return new RemoveOutcome(RemoveOutcomeStatus.Confirmed, removed, logsReopened);
    }

    public async Task UpgradeDatabaseAsync(
        string fileName,
        UpgradeProgressScope scope = UpgradeProgressScope.ManageDatabasesTriggered,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        cancellationToken.ThrowIfCancellationRequested();

        using (_upgradeGate.EnterScope())
        {
            if (_upgradesInFlight.Contains(fileName)) { return; }

            if (_upgradesInFlight.Count > 0) { return; }

            _upgradesInFlight.Add(fileName);
        }

        try
        {
            RaiseUpgradeStateChangedSafely();

            await RunOperationAsync(
                operationName: "upgrade",
                operation: new DatabaseOperation.UpgradeSingle(fileName),
                body: async () =>
                {
                    var result = await _databases.UpgradeBatchAsync([fileName], scope, cancellationToken);

                    foreach (var failure in result.Failed)
                    {
                        _errorBanners.ReportError(new DatabaseUpgradeFailed(failure.FileName, failure.Message));
                    }
                });
        }
        finally
        {
            using (_upgradeGate.EnterScope()) { _upgradesInFlight.Remove(fileName); }

            RaiseUpgradeStateChangedSafely();
        }
    }

    public async Task<UpgradeBatchResult?> UpgradeDatabasesAsync(
        IReadOnlyList<string> fileNames,
        UpgradeProgressScope scope = UpgradeProgressScope.ManageDatabasesTriggered,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fileNames);
        cancellationToken.ThrowIfCancellationRequested();

        if (fileNames.Count == 0)
        {
            return new UpgradeBatchResult(Succeeded: [], Cancelled: [], Failed: []);
        }

        using (_upgradeGate.EnterScope())
        {
            // Null means gate-denied by another upgrade, not a batch where every file was cancelled.
            if (_upgradesInFlight.Count > 0) { return null; }

            foreach (var fileName in fileNames) { _upgradesInFlight.Add(fileName); }
        }

        try
        {
            RaiseUpgradeStateChangedSafely();

            var fallback = new UpgradeBatchResult(Succeeded: [], Cancelled: [], Failed: []);

            return await RunOperationAsync(
                operationName: "upgrade",
                operation: new DatabaseOperation.UpgradeBatch(fileNames.Count),
                fallbackOutcome: fallback,
                body: async () =>
                {
                    var batchResult = await _databases.UpgradeBatchAsync(fileNames, scope, cancellationToken);

                    foreach (var failure in batchResult.Failed)
                    {
                        _errorBanners.ReportError(new DatabaseUpgradeFailed(failure.FileName, failure.Message));
                    }

                    return batchResult;
                });
        }
        finally
        {
            using (_upgradeGate.EnterScope())
            {
                foreach (var fileName in fileNames) { _upgradesInFlight.Remove(fileName); }
            }

            RaiseUpgradeStateChangedSafely();
        }
    }

    internal static DatabaseImportSummary BuildImportSummary(ImportResult importResult)
    {
        ArgumentNullException.ThrowIfNull(importResult);

        return new DatabaseImportSummary(
            importResult.Imported,
            importResult.Failures,
            importResult.UpgradeFailures);
    }

    private async Task EnableFreshlyImportedReadyDatabasesAsync(
        ImportResult result,
        CancellationToken cancellationToken)
    {
        if (result.Imported <= 0 || result.ImportedNames.Count == 0) { return; }

        var importedNames = result.ImportedNames.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var readyDisabledNames = _databases.Entries
            .Where(entry => importedNames.Contains(entry.FileName) && entry is { IsEnabled: false, Status: DatabaseStatus.Ready })
            .Select(entry => entry.FileName)
            .ToList();

        if (readyDisabledNames.Count == 0) { return; }

        await ApplyPendingTogglesAsync(readyDisabledNames, cancellationToken);

        if (_logReload.HasActiveLogs)
        {
            await _logReload.ReloadAllActiveLogsAsync(cancellationToken);
        }
    }

    private async Task<ImportOutcome> ImportPathsCoreAsync(
        IReadOnlyList<string> sourcePaths,
        bool enableOnImport,
        Func<string, CancellationToken, Task<bool>>? askOverwriteAsync,
        CancellationToken cancellationToken)
    {
        var skip = await ResolveImportConflictsAsync(sourcePaths, askOverwriteAsync, cancellationToken);
        var result = await _databases.ImportAsync(sourcePaths, skip, cancellationToken);
        var summary = BuildImportSummary(result);

        ReportPostOperationResult(summary);

        if (enableOnImport)
        {
            // Enable/reload failures must not turn a committed import into a reported import failure.
            try
            {
                await EnableFreshlyImportedReadyDatabasesAsync(result, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.Warning($"{nameof(ImportPathsCoreAsync)}: import succeeded but enable/reload did not complete: {ex}");
            }
        }

        return new ImportOutcome(result.Imported, result.Failures, result.UpgradeFailures);
    }

    private void RaiseUpgradeStateChangedSafely()
    {
        var handler = UpgradeStateChanged;

        if (handler is null) { return; }

        foreach (var subscriber in handler.GetInvocationList())
        {
            try { ((Action)subscriber).Invoke(); }
            catch (Exception ex)
            {
                _logger.Warning($"{nameof(RaiseUpgradeStateChangedSafely)} subscriber threw: {ex}");
            }
        }
    }

    private void ReportPostOperationResult(DatabaseImportSummary summary)
    {
        switch (summary.Severity)
        {
            case DatabaseImportSeverity.Info:
                _infoBanners.ReportInfoBanner(summary, BannerSeverity.Info);

                break;
            case DatabaseImportSeverity.Warning:
                _infoBanners.ReportInfoBanner(summary, BannerSeverity.Warning);

                break;
            case DatabaseImportSeverity.Error:
                _errorBanners.ReportError(summary);

                break;
        }
    }

    private async Task<IReadOnlySet<string>> ResolveImportConflictsAsync(
        IReadOnlyList<string> sourcePaths,
        Func<string, CancellationToken, Task<bool>>? askOverwriteAsync,
        CancellationToken cancellationToken)
    {
        var existingNames = _databases.Entries
            .Select(entry => entry.FileName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var pendingNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var skip = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var resolved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var sourcePath in sourcePaths)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrEmpty(sourcePath)) { continue; }

            IReadOnlyList<string> candidateNames;

            if (Path.GetExtension(sourcePath).Equals(".zip", StringComparison.OrdinalIgnoreCase))
            {
                candidateNames = await _databases.EnumerateZipDbEntryNamesAsync(sourcePath, cancellationToken);
            }
            else
            {
                var name = Path.GetFileName(sourcePath);

                candidateNames = string.IsNullOrEmpty(name) ? [] : [name];
            }

            foreach (var candidateName in candidateNames)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (string.IsNullOrEmpty(candidateName)) { continue; }

                bool batchDuplicate = !pendingNames.Add(candidateName);

                if (batchDuplicate)
                {
                    skip.Add(candidateName);

                    continue;
                }

                if (!existingNames.Contains(candidateName)) { continue; }

                if (!resolved.Add(candidateName)) { continue; }

                if (askOverwriteAsync is null)
                {
                    skip.Add(candidateName);

                    continue;
                }

                bool overwrite;

                try { overwrite = await askOverwriteAsync(candidateName, cancellationToken); }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    // Defensive: unexpected callback exceptions are treated as Skip and logged.
                    _logger.Warning($"{nameof(ResolveImportConflictsAsync)} overwrite callback threw for '{candidateName}': {ex}");
                    skip.Add(candidateName);

                    continue;
                }

                if (!overwrite) { skip.Add(candidateName); }
            }
        }

        return skip;
    }

    private async Task<T> RunOperationAsync<T>(
        string operationName,
        DatabaseOperation operation,
        T fallbackOutcome,
        Func<Task<T>> body)
    {
        try { return await body(); }
        catch (OperationCanceledException) { return fallbackOutcome; }
        catch (Exception ex)
        {
            _logger.Warning($"{operationName} failed: {ex}");
            _errorBanners.ReportError(new DatabaseOperationFailed(operation, ex.Message));

            return fallbackOutcome;
        }
    }

    private async Task RunOperationAsync(
        string operationName,
        DatabaseOperation operation,
        Func<Task> body)
    {
        try { await body(); }
        catch (OperationCanceledException)
        {
            // Cancellation is silent.
        }
        catch (Exception ex)
        {
            _logger.Warning($"{operationName} failed: {ex}");
            _errorBanners.ReportError(new DatabaseOperationFailed(operation, ex.Message));
        }
    }
}
