// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;

namespace EventLogExpert.DatabaseTools.Common.Operations;

public interface IDatabaseToolsOperation
{
    string? FailureSummary => null;

    Task<DatabaseToolsOutcome> ExecuteAsync(ITraceLogger logger, IProgress<DatabaseToolsProgress>? progress, CancellationToken cancellationToken);
}
