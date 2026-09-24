// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.DatabaseTools.Common;
using EventLogExpert.DatabaseTools.Common.Operations;
using EventLogExpert.DatabaseTools.CreateDatabase;
using EventLogExpert.DatabaseTools.DiffDatabase;
using EventLogExpert.DatabaseTools.MergeDatabase;
using EventLogExpert.DatabaseTools.ShowProviders;
using EventLogExpert.DatabaseTools.UpgradeDatabase;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Logging.Loggers;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace EventLogExpert.Runtime.DatabaseTools;

internal sealed class DatabaseToolsService(IDatabaseToolsOperationFactory factory)
    : IDatabaseToolsService
{
    public Task<DatabaseToolsResult> CreateAsync(
        CreateDatabaseRequest request,
        IProgress<LogRecord> logProgress,
        IProgress<DatabaseToolsProgress>? progress,
        CancellationToken cancellationToken,
        bool verbose = false)
        => RunAsync(factory.Create(request), logProgress, progress, cancellationToken, verbose);

    public Task<DatabaseToolsResult> DiffAsync(
        DiffDatabaseRequest request,
        IProgress<LogRecord> logProgress,
        IProgress<DatabaseToolsProgress>? progress,
        CancellationToken cancellationToken,
        bool verbose = false)
        => RunAsync(factory.Create(request), logProgress, progress, cancellationToken, verbose);

    public Task<DatabaseToolsResult> MergeAsync(
        MergeDatabaseRequest request,
        IProgress<LogRecord> logProgress,
        IProgress<DatabaseToolsProgress>? progress,
        CancellationToken cancellationToken,
        bool verbose = false)
        => RunAsync(factory.Create(request), logProgress, progress, cancellationToken, verbose);

    public Task<DatabaseToolsResult> ShowAsync(
        ShowProvidersRequest request,
        IProgress<LogRecord> logProgress,
        IProgress<DatabaseToolsProgress>? progress,
        CancellationToken cancellationToken,
        bool verbose = false)
        => RunAsync(factory.Create(request), logProgress, progress, cancellationToken, verbose);

    public Task<DatabaseToolsResult> UpgradeAsync(
        UpgradeDatabaseRequest request,
        IProgress<LogRecord> logProgress,
        IProgress<DatabaseToolsProgress>? progress,
        CancellationToken cancellationToken,
        bool verbose = false)
        => RunAsync(factory.Create(request), logProgress, progress, cancellationToken, verbose);

    private async Task<DatabaseToolsResult> RunAsync(
        IDatabaseToolsOperation operation,
        IProgress<LogRecord> logProgress,
        IProgress<DatabaseToolsProgress>? progress,
        CancellationToken cancellationToken,
        bool verbose)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(logProgress);

        IOperationLog operationLog = new StreamingOperationLog(logProgress, verbose ? LogLevel.Trace : LogLevel.Information);
        var startTimestamp = Stopwatch.GetTimestamp();

        DatabaseToolsOutcome outcome;
        LocalizableText? summary = null;
        bool diagnostic = false;
        string? diagnosticDetail = null;

        try
        {
            outcome = await Task.Run(
                () => operation.ExecuteAsync(operationLog, progress, cancellationToken),
                cancellationToken);

            summary = operation.FailureSummary;
        }
        catch (OperationCanceledException)
        {
            outcome = DatabaseToolsOutcome.Cancelled;
        }
        catch (Exception ex)
        {
            outcome = DatabaseToolsOutcome.Failed;
            summary = new LocalizableText(DatabaseToolsLogKeys.GenericDiagnosticFailure, []);
            diagnostic = true;
            diagnosticDetail = ex.ToString();
        }

        return new DatabaseToolsResult(outcome, summary, Stopwatch.GetElapsedTime(startTimestamp))
        {
            SummaryIsDiagnostic = diagnostic,
            DiagnosticDetail = diagnosticDetail
        };
    }
}
