// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.DatabaseTools.Common.Operations;
using EventLogExpert.DatabaseTools.CreateDatabase;
using EventLogExpert.DatabaseTools.DiffDatabase;
using EventLogExpert.DatabaseTools.MergeDatabase;
using EventLogExpert.DatabaseTools.ShowProviders;
using EventLogExpert.DatabaseTools.UpgradeDatabase;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Logging.Routing;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace EventLogExpert.Runtime.DatabaseTools;

internal sealed class DatabaseToolsService(IDatabaseToolsOperationFactory factory) : IDatabaseToolsService
{
    public Task<DatabaseToolsResult> CreateAsync(
        CreateDatabaseRequest request,
        IProgress<LogRecord> logSink,
        IProgress<DatabaseToolsProgress>? progress,
        CancellationToken cancellationToken,
        bool verbose = false)
        => RunAsync(factory.Create(request), logSink, progress, cancellationToken, verbose);

    public Task<DatabaseToolsResult> DiffAsync(
        DiffDatabaseRequest request,
        IProgress<LogRecord> logSink,
        IProgress<DatabaseToolsProgress>? progress,
        CancellationToken cancellationToken,
        bool verbose = false)
        => RunAsync(factory.Create(request), logSink, progress, cancellationToken, verbose);

    public Task<DatabaseToolsResult> MergeAsync(
        MergeDatabaseRequest request,
        IProgress<LogRecord> logSink,
        IProgress<DatabaseToolsProgress>? progress,
        CancellationToken cancellationToken,
        bool verbose = false)
        => RunAsync(factory.Create(request), logSink, progress, cancellationToken, verbose);

    public Task<DatabaseToolsResult> ShowAsync(
        ShowProvidersRequest request,
        IProgress<LogRecord> logSink,
        IProgress<DatabaseToolsProgress>? progress,
        CancellationToken cancellationToken,
        bool verbose = false)
        => RunAsync(factory.Create(request), logSink, progress, cancellationToken, verbose);

    public Task<DatabaseToolsResult> UpgradeAsync(
        UpgradeDatabaseRequest request,
        IProgress<LogRecord> logSink,
        IProgress<DatabaseToolsProgress>? progress,
        CancellationToken cancellationToken,
        bool verbose = false)
        => RunAsync(factory.Create(request), logSink, progress, cancellationToken, verbose);

    private static async Task<DatabaseToolsResult> RunAsync(
        IDatabaseToolsOperation operation,
        IProgress<LogRecord> logSink,
        IProgress<DatabaseToolsProgress>? progress,
        CancellationToken cancellationToken,
        bool verbose)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(logSink);

        ITraceLogger logger = new StreamingTraceLogger(logSink, verbose ? LogLevel.Trace : LogLevel.Information);
        var startTimestamp = Stopwatch.GetTimestamp();

        DatabaseToolsOutcome outcome;
        string? failureSummary = null;

        try
        {
            outcome = await Task.Run(
                () => operation.ExecuteAsync(logger, progress, cancellationToken),
                cancellationToken);

            // FailureSummary carries actionable UI text when operations fail without throwing.
            failureSummary = operation.FailureSummary;
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

        return new DatabaseToolsResult(outcome, failureSummary, Stopwatch.GetElapsedTime(startTimestamp));
    }
}
