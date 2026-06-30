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
        // Scripts depend on Failed never mapping to 0 and Cancelled remaining distinct from hard failure.
        Assert.Equal(expectedExitCode, CommandExitCode.ToExitCode(outcome));
    }
}
