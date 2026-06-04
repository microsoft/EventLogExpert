// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;

namespace EventLogExpert.DatabaseTools.Common.Operations;

/// <summary>
///     Behavioral contract for a single DatabaseTools operation (Show / Create / Merge / Diff / Upgrade). The
///     <see cref="ExecuteAsync" /> method owns its own <c>DbContext</c> lifetime on the worker thread, checks the
///     cancellation token cooperatively between units of work, and returns an explicit <see cref="DatabaseToolsOutcome" />
///     — the outcome is NOT inferred from log severity.
/// </summary>
public interface IDatabaseToolsOperation
{
    /// <summary>
    ///     Executes the operation. All informational, warning, and error messages are emitted via
    ///     <paramref name="logger" /> (which streams to the UI). Optional progress updates flow via
    ///     <paramref name="progress" />. Cancellation is cooperative: implementations check the token at iteration boundaries
    ///     and pass it through to async EF Core calls.
    /// </summary>
    Task<DatabaseToolsOutcome> ExecuteAsync(ITraceLogger logger, IProgress<DatabaseToolsProgress>? progress, CancellationToken cancellationToken);
}
