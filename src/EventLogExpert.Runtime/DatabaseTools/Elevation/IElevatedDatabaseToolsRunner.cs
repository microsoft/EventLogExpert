// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.DatabaseTools.Common.Operations;
using EventLogExpert.DatabaseTools.CreateDatabase;
using EventLogExpert.DatabaseTools.DiffDatabase;
using EventLogExpert.DatabaseTools.MergeDatabase;
using EventLogExpert.DatabaseTools.ShowProviders;
using EventLogExpert.DatabaseTools.UpgradeDatabase;
using EventLogExpert.Logging.Abstractions;

namespace EventLogExpert.Runtime.DatabaseTools.Elevation;

/// <summary>
///     Runner facade for executing a DatabaseTools operation in an elevated child process. Mirrors the
///     <see cref="IDatabaseToolsService" /> shape so a tab's dispatch site can choose between in-process (medium-IL)
///     execution and elevated (high-IL) execution by selecting one of the two facades - same request types, same outcome
///     record, same streaming-log/progress callbacks.
/// </summary>
/// <remarks>
///     Implementations spawn the packaged elevation helper EXE via <see cref="IElevatedHelperProcessHost" />, forward
///     the request through the named-pipe IPC channel, stream incoming <c>LogMessage</c> / <c>ProgressMessage</c> records
///     into the caller-supplied <see cref="IProgress{T}" /> sinks, await the terminal <c>ResultMessage</c>, and translate
///     UAC-decline into <see cref="DatabaseToolsOutcome.Cancelled" />. Cancellation is cooperative - see
///     <c>ElevatedDatabaseToolsRunner</c>'s remarks for the cancel message / 30s grace / force-kill sequence.
/// </remarks>
public interface IElevatedDatabaseToolsRunner
{
    Task<DatabaseToolsResult> CreateAsync(
        CreateDatabaseRequest request,
        IProgress<LogRecord> logSink,
        IProgress<DatabaseToolsProgress>? progress,
        CancellationToken cancellationToken,
        bool verbose = false);

    Task<DatabaseToolsResult> DiffAsync(
        DiffDatabaseRequest request,
        IProgress<LogRecord> logSink,
        IProgress<DatabaseToolsProgress>? progress,
        CancellationToken cancellationToken,
        bool verbose = false);

    Task<DatabaseToolsResult> MergeAsync(
        MergeDatabaseRequest request,
        IProgress<LogRecord> logSink,
        IProgress<DatabaseToolsProgress>? progress,
        CancellationToken cancellationToken,
        bool verbose = false);

    Task<DatabaseToolsResult> ShowAsync(
        ShowProvidersRequest request,
        IProgress<LogRecord> logSink,
        IProgress<DatabaseToolsProgress>? progress,
        CancellationToken cancellationToken,
        bool verbose = false);

    Task<DatabaseToolsResult> UpgradeAsync(
        UpgradeDatabaseRequest request,
        IProgress<LogRecord> logSink,
        IProgress<DatabaseToolsProgress>? progress,
        CancellationToken cancellationToken,
        bool verbose = false);
}
