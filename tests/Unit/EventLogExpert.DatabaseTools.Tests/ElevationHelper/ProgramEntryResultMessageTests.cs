// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.DatabaseTools.Common.Operations;
using EventLogExpert.ElevationHelper;
using EventLogExpert.Logging.Abstractions;

namespace EventLogExpert.DatabaseTools.Tests.ElevationHelper;

public sealed class ProgramEntryResultMessageTests
{
    [Fact]
    public void BuildResultMessage_CarriesDiagnosticFlagAndFields_WhenResultIsDiagnostic()
    {
        var result = new DatabaseToolsResult(
            DatabaseToolsOutcome.Failed,
            new LocalizableText("DatabaseTools_Op_TestFailure", ["device"]),
            TimeSpan.FromMilliseconds(1234))
        {
            SummaryIsDiagnostic = true
        };

        var message = ProgramEntry.BuildResultMessage(result);

        Assert.Equal(DatabaseToolsOutcome.Failed, message.Outcome);
        Assert.Equal("DatabaseTools_Op_TestFailure", message.SummaryKey);
        Assert.Equal(["device"], message.SummaryArgs);
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
            new LocalizableText("DatabaseTools_Op_TestActionable", ["3"]),
            TimeSpan.Zero)
        {
            SummaryIsDiagnostic = false
        };

        var message = ProgramEntry.BuildResultMessage(result);

        Assert.Equal(DatabaseToolsOutcome.Failed, message.Outcome);
        Assert.Equal("DatabaseTools_Op_TestActionable", message.SummaryKey);
        Assert.Equal(["3"], message.SummaryArgs);
        Assert.False(message.SummaryIsDiagnostic);
    }
}
