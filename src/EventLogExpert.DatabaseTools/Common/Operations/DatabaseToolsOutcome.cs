// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.DatabaseTools.Common.Operations;

/// <summary>Final outcome of a DatabaseTools operation. Explicit (not inferred from log severity).</summary>
public enum DatabaseToolsOutcome
{
    Succeeded,
    Failed,
    Cancelled
}
