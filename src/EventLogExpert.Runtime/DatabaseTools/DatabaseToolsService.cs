// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.DatabaseTools.Contracts;
using EventLogExpert.DatabaseTools.Operations;
using EventLogExpert.Logging.Abstractions;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace EventLogExpert.Runtime.DatabaseTools;

/// <summary>
///     Default <see cref="IDatabaseToolsService" /> implementation. Dispatches each Operation on
///     <see cref="Task.Run(System.Action)" /> so the calling (UI) thread is not blocked; wraps the Operation in a
///     <see cref="StreamingTraceLogger" /> bound to the caller-supplied <see cref="IProgress{T}" /> sink so log entries
///     stream as they are emitted; catches <see cref="OperationCanceledException" /> and other exceptions to produce an
///     explicit <see cref="DatabaseToolsResult" /> rather than propagating into the UI's <see cref="Task" />.
/// </summary>
internal sealed class DatabaseToolsService : IDatabaseToolsService
{
    public Task<DatabaseToolsResult> CreateAsync(
        CreateDatabaseRequest request,
        IProgress<DatabaseToolsLogEntry> logSink,
        IProgress<DatabaseToolsProgress>? progress,
        CancellationToken cancellationToken,
        bool verbose = false)
        => RunAsync(new CreateDatabaseOperation(request), logSink, progress, cancellationToken, verbose);

    public Task<DatabaseToolsResult> DiffAsync(
        DiffDatabaseRequest request,
        IProgress<DatabaseToolsLogEntry> logSink,
        IProgress<DatabaseToolsProgress>? progress,
        CancellationToken cancellationToken,
        bool verbose = false)
        => RunAsync(new DiffDatabaseOperation(request), logSink, progress, cancellationToken, verbose);

    public Task<DatabaseToolsResult> MergeAsync(
        MergeDatabaseRequest request,
        IProgress<DatabaseToolsLogEntry> logSink,
        IProgress<DatabaseToolsProgress>? progress,
        CancellationToken cancellationToken,
        bool verbose = false)
        => RunAsync(new MergeDatabaseOperation(request), logSink, progress, cancellationToken, verbose);

    public Task<DatabaseToolsResult> ShowAsync(
        ShowProvidersRequest request,
        IProgress<DatabaseToolsLogEntry> logSink,
        IProgress<DatabaseToolsProgress>? progress,
        CancellationToken cancellationToken,
        bool verbose = false)
        => RunAsync(new ShowProvidersOperation(request), logSink, progress, cancellationToken, verbose);

    public Task<DatabaseToolsResult> UpgradeAsync(
        UpgradeDatabaseRequest request,
        IProgress<DatabaseToolsLogEntry> logSink,
        IProgress<DatabaseToolsProgress>? progress,
        CancellationToken cancellationToken,
        bool verbose = false)
        => RunAsync(new UpgradeDatabaseOperation(request), logSink, progress, cancellationToken, verbose);

    private static async Task<DatabaseToolsResult> RunAsync(
        IDatabaseToolsOperation operation,
        IProgress<DatabaseToolsLogEntry> logSink,
        IProgress<DatabaseToolsProgress>? progress,
        CancellationToken cancellationToken,
        bool verbose)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(logSink);

        ITraceLogger logger = new StreamingTraceLogger(logSink, verbose ? LogLevel.Trace : LogLevel.Information);
        var stopwatch = Stopwatch.StartNew();

        DatabaseToolsOutcome outcome;
        string? failureSummary = null;

        try
        {
            outcome = await Task.Run(
                () => operation.ExecuteAsync(logger, progress, cancellationToken),
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            outcome = DatabaseToolsOutcome.Cancelled;
        }
        catch (Exception ex)
        {
            outcome = DatabaseToolsOutcome.Failed;
            failureSummary = ex.Message;
            logger.Error($"{ex}");
        }
        finally
        {
            stopwatch.Stop();
        }

        return new DatabaseToolsResult(outcome, failureSummary, stopwatch.Elapsed);
    }
}
