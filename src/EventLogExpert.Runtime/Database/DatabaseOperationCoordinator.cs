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
    private readonly ITraceLogger _logger = logger;
    private readonly ILogReloadCoordinator _logReload = logReload;
    private readonly Lock _upgradeGate = new();
    private readonly HashSet<string> _upgradesInFlight = new(StringComparer.OrdinalIgnoreCase);

    public event Action? UpgradeStateChanged;

    internal enum ResultSeverity
    {
        Info,
        Warning,
        Error,
    }

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
                failureTitle: "Failed to Update Database",
                operationNoun: $"updating '{fileName}'",
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
            failureTitle: "Import Failed",
            operationNoun: "importing provider databases",
            fallbackOutcome: ImportOutcome.None,
            body: async () =>
            {
                var sourcePaths = await _filePicker.PickMultipleAsync(
                    "Please select database files to import",
                    FilePickerFileTypes.Database);

                if (sourcePaths.Count == 0) { return ImportOutcome.None; }

                var skip = await ResolveImportConflictsAsync(sourcePaths, askOverwriteAsync, cancellationToken);
                var result = await _databases.ImportAsync(sourcePaths, skip, cancellationToken);
                var (title, message, severity) = BuildImportSummary(result);

                ReportPostOperationResult(title, message, severity);

                return new ImportOutcome(result.Imported, result.Failures, result.UpgradeFailures);
            });
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

        // Remove is bespoke (not via RunOperationAsync) because of the snapshot-reopen-on-failure semantic:
        // if RemoveAsync throws after populating snapshot.Items, logs that were closed must still reopen.
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
            _errorBanners.ReportError(
                "Failed to Remove Database",
                $"An exception occurred while removing '{fileName}': {ex.Message}");
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
                failureTitle: "Database Upgrade Failed",
                operationNoun: $"upgrading '{fileName}'",
                body: async () =>
                {
                    var result = await _databases.UpgradeBatchAsync([fileName], scope, cancellationToken);

                    foreach (var failure in result.Failed)
                    {
                        _errorBanners.ReportError(
                            "Database Upgrade Failed",
                            $"Failed to upgrade '{failure.FileName}': {failure.Message}");
                    }
                });
        }
        finally
        {
            using (_upgradeGate.EnterScope()) { _upgradesInFlight.Remove(fileName); }

            RaiseUpgradeStateChangedSafely();
        }
    }

    internal static (string Title, string Message, ResultSeverity Severity) BuildImportSummary(ImportResult importResult)
    {
        ArgumentNullException.ThrowIfNull(importResult);

        var imported = importResult.Imported;
        var failures = importResult.Failures;
        var upgradeFailures = importResult.UpgradeFailures;

        if (failures.Count == 0 && upgradeFailures.Count == 0)
        {
            if (imported == 0) { return ("Import Successful", "No databases were imported.", ResultSeverity.Info); }

            var successMessage = imported > 1
                ? $"{imported} databases have successfully been imported"
                : "1 database has successfully been imported";

            return ("Import Successful", successMessage, ResultSeverity.Info);
        }

        var failureSummary = FormatFailureSummary(failures, upgradeFailures);

        if (imported == 0)
        {
            return ("Import Failed", $"No databases were imported; {failureSummary}", ResultSeverity.Error);
        }

        var partialMessage = imported > 1
            ? $"{imported} databases imported"
            : "1 database imported";

        return ("Import Completed with Errors", $"{partialMessage}; {failureSummary}", ResultSeverity.Warning);
    }

    private static string FormatFailureSummary(
        IReadOnlyList<ImportFailure> failures,
        IReadOnlyList<ImportFailure> upgradeFailures)
    {
        var parts = new List<string>(failures.Count + upgradeFailures.Count);

        foreach (var entry in failures) { parts.Add($"{entry.FileName} ({entry.Reason})"); }

        foreach (var entry in upgradeFailures) { parts.Add($"{entry.FileName} upgrade ({entry.Reason})"); }

        return $"failed: {string.Join(", ", parts)}";
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

    private void ReportPostOperationResult(string title, string message, ResultSeverity severity)
    {
        switch (severity)
        {
            case ResultSeverity.Info:
                _infoBanners.ReportInfoBanner(title, message, BannerSeverity.Info);

                break;
            case ResultSeverity.Warning:
                _infoBanners.ReportInfoBanner(title, message, BannerSeverity.Warning);

                break;
            case ResultSeverity.Error:
                _errorBanners.ReportError(title, message);

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

        if (existingNames.Count == 0) { return new HashSet<string>(StringComparer.OrdinalIgnoreCase); }

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
                    // Defensive: unexpected callback exception → treat as Skip and log.
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
        string failureTitle,
        string operationNoun,
        T fallbackOutcome,
        Func<Task<T>> body)
    {
        try { return await body(); }
        catch (OperationCanceledException) { return fallbackOutcome; }
        catch (Exception ex)
        {
            _logger.Warning($"{operationName} failed: {ex}");
            _errorBanners.ReportError(
                failureTitle,
                $"An exception occurred while {operationNoun}: {ex.Message}");

            return fallbackOutcome;
        }
    }

    private async Task RunOperationAsync(
        string operationName,
        string failureTitle,
        string operationNoun,
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
            _errorBanners.ReportError(
                failureTitle,
                $"An exception occurred while {operationNoun}: {ex.Message}");
        }
    }
}
