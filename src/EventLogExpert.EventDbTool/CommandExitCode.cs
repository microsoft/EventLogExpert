// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.DatabaseTools.Common.Operations;

namespace EventLogExpert.EventDbTool;

internal static class CommandExitCode
{
    public static int ToExitCode(DatabaseToolsOutcome outcome) => outcome switch
    {
        DatabaseToolsOutcome.Succeeded => 0,
        DatabaseToolsOutcome.Cancelled => 2,
        _ => 1,
    };
}
