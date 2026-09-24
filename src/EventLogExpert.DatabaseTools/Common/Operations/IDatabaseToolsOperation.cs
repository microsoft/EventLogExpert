// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;

namespace EventLogExpert.DatabaseTools.Common.Operations;

public interface IDatabaseToolsOperation
{
    LocalizableText? FailureSummary => null;

    Task<DatabaseToolsOutcome> ExecuteAsync(
        IOperationLog log,
        IProgress<DatabaseToolsProgress>? progress,
        CancellationToken cancellationToken);
}
