// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.DatabaseTools.Common.Operations;
using EventLogExpert.ElevationHelper;

namespace EventLogExpert.DatabaseTools.Tests.ElevationHelper;

public sealed class ProgramEntryResultMessageTests
{
    [Fact]
    public void BuildResultMessage_CarriesDiagnosticFlagAndFields_WhenResultIsDiagnostic()
    {
        var result = new DatabaseToolsResult(
            DatabaseToolsOutcome.Failed,
            "IOException: The device is not ready.",
            TimeSpan.FromMilliseconds(1234))
        {
            SummaryIsDiagnostic = true
        };

        var message = ProgramEntry.BuildResultMessage(result);

        Assert.Equal(DatabaseToolsOutcome.Failed, message.Outcome);
        Assert.Equal("IOException: The device is not ready.", message.FailureSummary);
        Assert.Equal(1234, message.DurationMs);

        // Guards the cross-process copy: if the flag were dropped/hardcoded here, a helper-side thrown-exception
        // failure would revert to leaking English exception text into the localized operation log.
        Assert.True(message.SummaryIsDiagnostic);
    }

    [Fact]
    public void BuildResultMessage_LeavesActionableSummaryUnmarked_WhenResultIsNotDiagnostic()
    {
        var result = new DatabaseToolsResult(
            DatabaseToolsOutcome.Failed,
            "3 providers failed to import.",
            TimeSpan.Zero)
        {
            SummaryIsDiagnostic = false
        };

        var message = ProgramEntry.BuildResultMessage(result);

        Assert.Equal(DatabaseToolsOutcome.Failed, message.Outcome);
        Assert.Equal("3 providers failed to import.", message.FailureSummary);
        Assert.False(message.SummaryIsDiagnostic);
    }
}
