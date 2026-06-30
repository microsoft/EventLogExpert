// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.DatabaseTools.Common.Operations;

namespace EventLogExpert.EventDbTool.Tests;

public sealed class CommandExitCodeTests
{
    [Theory]
    [InlineData(DatabaseToolsOutcome.Succeeded, 0)]
    [InlineData(DatabaseToolsOutcome.Failed, 1)]
    [InlineData(DatabaseToolsOutcome.Cancelled, 2)]
    public void ToExitCode_MapsOutcomeToProcessExitCode(DatabaseToolsOutcome outcome, int expectedExitCode)
    {
        // The CLI propagates these codes to callers and scripts; a Failed operation must never exit 0 (the bug this
        // fixes), and a Cancelled run is distinguishable from a hard failure.
        Assert.Equal(expectedExitCode, CommandExitCode.ToExitCode(outcome));
    }
}
