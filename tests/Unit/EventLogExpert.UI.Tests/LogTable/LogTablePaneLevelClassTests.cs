// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.LogTable;

namespace EventLogExpert.UI.Tests.LogTable;

public sealed class LogTablePaneLevelClassTests
{
    // Pins the level -> icon/colour class mapping (delegates to the shared SeverityIcon map via
    // LevelSeverity.FromLevelName): every known level gets a distinct shape; Error/Warning/Critical/Verbose add a
    // colour class, Information is icon-only; miscased/unknown parse to null and fall through to "".
    [Theory]
    [InlineData("Critical", "bi bi-exclamation-octagon-fill critical")]
    [InlineData("Error", "bi bi-exclamation-circle error")]
    [InlineData("Warning", "bi bi-exclamation-triangle warning")]
    [InlineData("Information", "bi bi-info-circle")]
    [InlineData("Verbose", "bi bi-circle verbose")]
    [InlineData("error", "")]
    [InlineData("Audit Success", "")]
    [InlineData("", "")]
    public void GetLevelClass_ReturnsExpectedClassString(string level, string expected) =>
        Assert.Equal(expected, LogTablePane.GetLevelClass(level));
}
