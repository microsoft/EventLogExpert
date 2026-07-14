// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.LogTable;

namespace EventLogExpert.UI.Tests.LogTable;

public sealed class LogTablePaneLevelClassTests
{
    // Pins the level -> icon/colour class mapping after it was refactored to consume the shared
    // LevelSeverity.FromLevelName parse: Error/Warning keep their colour class, Information is icon-only, and
    // Critical/Verbose/miscased/unknown fall through to "" exactly as the pre-refactor case-sensitive switch did.
    [Theory]
    [InlineData("Error", "bi bi-exclamation-circle error")]
    [InlineData("Warning", "bi bi-exclamation-triangle warning")]
    [InlineData("Information", "bi bi-info-circle")]
    [InlineData("Critical", "")]
    [InlineData("Verbose", "")]
    [InlineData("error", "")]
    [InlineData("Audit Success", "")]
    [InlineData("", "")]
    public void GetLevelClass_ReturnsExpectedClassString(string level, string expected) =>
        Assert.Equal(expected, LogTablePane.GetLevelClass(level));
}
