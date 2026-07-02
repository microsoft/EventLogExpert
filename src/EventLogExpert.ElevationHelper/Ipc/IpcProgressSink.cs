// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.DatabaseTools.Common.Ipc;
using EventLogExpert.DatabaseTools.Common.Operations;

namespace EventLogExpert.ElevationHelper.Ipc;

/// <summary>
///     Helper-side <see cref="IProgress{T}" /> implementation for <see cref="DatabaseToolsProgress" />. Same contract
///     as <see cref="IpcLogForwarder" />: wraps each report in a <see cref="ProgressMessage" /> and writes it through the
///     shared semaphore-guarded <see cref="IpcMessageWriter" />.
/// </summary>
internal sealed class IpcProgressSink(IpcMessageWriter writer) : IProgress<DatabaseToolsProgress>
{
    public void Report(DatabaseToolsProgress value)
    {
        try
        {
            writer.WriteAsync(new ProgressMessage(value.Processed, value.Total, value.CurrentItem), CancellationToken.None)
                .GetAwaiter()
                .GetResult();
        }
        catch
        {
            // Pipe closed mid-write (runner gone). Drop the report.
        }
    }
}
