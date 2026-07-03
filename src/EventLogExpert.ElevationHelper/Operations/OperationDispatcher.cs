// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.DatabaseTools.Common.Ipc;
using EventLogExpert.DatabaseTools.Common.Operations;
using EventLogExpert.ElevationHelper.Ipc;
using EventLogExpert.Logging.Abstractions;
using EventLogExpert.Runtime.DatabaseTools;
using Microsoft.Extensions.DependencyInjection;

namespace EventLogExpert.ElevationHelper.Operations;

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
        var logProgress = new IpcLogForwarder(writer);
        var progress = new IpcProgressForwarder(writer);

        // List-editions is read-only, so it bypasses destructive database recovery.
        if (request is ListImageEditionsIpcRequest listEditions)
        {
            return await ListImageEditionsHandler.HandleAsync(listEditions.Request, writer, listEditions.Verbose, cancellationToken);
        }

        return await DestructiveRecovery.WrapAsync(
            request,
            (req, ct) => RawDispatchAsync(service, req, logProgress, progress, ct),
            cancellationToken);
    }

    private static Task<DatabaseToolsResult> RawDispatchAsync(
        IDatabaseToolsService service,
        DatabaseToolsIpcRequest request,
        IProgress<LogRecord> logProgress,
        IProgress<DatabaseToolsProgress> progress,
        CancellationToken cancellationToken)
        => request switch
        {
            ShowProvidersIpcRequest s => service.ShowAsync(s.Request, logProgress, progress, cancellationToken, s.Verbose),
            CreateDatabaseIpcRequest c => service.CreateAsync(c.Request, logProgress, progress, cancellationToken, c.Verbose),
            MergeDatabaseIpcRequest m => service.MergeAsync(m.Request, logProgress, progress, cancellationToken, m.Verbose),
            DiffDatabaseIpcRequest d => service.DiffAsync(d.Request, logProgress, progress, cancellationToken, d.Verbose),
            UpgradeDatabaseIpcRequest u => service.UpgradeAsync(u.Request, logProgress, progress, cancellationToken, u.Verbose),
            _ => Task.FromResult(new DatabaseToolsResult(DatabaseToolsOutcome.Failed, $"Unknown request type: {request.GetType().Name}", TimeSpan.Zero))
        };
}

