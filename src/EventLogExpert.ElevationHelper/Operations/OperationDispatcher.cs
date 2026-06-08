// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.DatabaseTools.Common.Ipc;
using EventLogExpert.DatabaseTools.Common.Operations;
using EventLogExpert.ElevationHelper.Ipc;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.DatabaseTools;
using Microsoft.Extensions.DependencyInjection;

namespace EventLogExpert.ElevationHelper.Operations;

/// <summary>
///     Helper-side operation dispatcher. Given an already-deserialized <see cref="DatabaseToolsIpcRequest" />,
///     resolves <see cref="IDatabaseToolsService" /> via the helper-friendly <c>AddDatabaseToolsRuntime</c> DI extension,
///     dispatches to the correct service method per request type, and returns the final <see cref="DatabaseToolsResult" />
///     . Destructive-recovery wrapping (Create/Diff output cleanup on failure, Upgrade .bak/restore) lives in
///     <see cref="DestructiveRecovery" /> and is applied transparently before the result is returned.
/// </summary>
/// <remarks>
///     Request reading happens in <see cref="ProgramEntry" /> (so the helper can start a control-reader task on the
///     same pipe AFTER the request is consumed and BEFORE this dispatch starts; that control reader watches for
///     <see cref="CancelMessage" /> and cancels the operation CT).
/// </remarks>
internal static class OperationDispatcher
{
    public static async Task<DatabaseToolsResult> DispatchAsync(
        DatabaseToolsIpcRequest request,
        IpcMessageWriter writer,
        CancellationToken cancellationToken)
    {
        await using var services = new ServiceCollection()
            .AddDatabaseToolsRuntime()
            .BuildServiceProvider();

        var service = services.GetRequiredService<IDatabaseToolsService>();
        var logSink = new IpcLogSink(writer);
        var progressSink = new IpcProgressSink(writer);

        return await DestructiveRecovery.WrapAsync(
            request,
            (req, ct) => RawDispatchAsync(service, req, logSink, progressSink, ct),
            cancellationToken);
    }

    private static Task<DatabaseToolsResult> RawDispatchAsync(
        IDatabaseToolsService service,
        DatabaseToolsIpcRequest request,
        IProgress<LogRecord> logRecord,
        IProgress<DatabaseToolsProgress> progress,
        CancellationToken cancellationToken)
        => request switch
        {
            ShowProvidersIpcRequest s => service.ShowAsync(s.Request, logRecord, progress, cancellationToken, s.Verbose),
            CreateDatabaseIpcRequest c => service.CreateAsync(c.Request, logRecord, progress, cancellationToken, c.Verbose),
            MergeDatabaseIpcRequest m => service.MergeAsync(m.Request, logRecord, progress, cancellationToken, m.Verbose),
            DiffDatabaseIpcRequest d => service.DiffAsync(d.Request, logRecord, progress, cancellationToken, d.Verbose),
            UpgradeDatabaseIpcRequest u => service.UpgradeAsync(u.Request, logRecord, progress, cancellationToken, u.Verbose),
            _ => Task.FromResult(new DatabaseToolsResult(DatabaseToolsOutcome.Failed, $"Unknown request type: {request.GetType().Name}", TimeSpan.Zero))
        };
}

