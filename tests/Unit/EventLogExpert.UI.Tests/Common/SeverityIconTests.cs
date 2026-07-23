// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Common.Events;
using EventLogExpert.UI.Common;

namespace EventLogExpert.UI.Tests.Common;

public sealed class SeverityIconTests
{
    [Fact]
    public void CssClass_EveryKnownLevelUsesADistinctGlyph()
    {
        SeverityLevel[] levels =
            [SeverityLevel.Critical, SeverityLevel.Error, SeverityLevel.Warning, SeverityLevel.Information, SeverityLevel.Verbose];

        var classes = levels.Select(level => SeverityIcon.CssClass(level)).ToArray();

        Assert.Equal(classes.Length, classes.Distinct().Count());
    }

    [Fact]
    public void CssClass_ForNull_ReturnsEmpty() => Assert.Equal(string.Empty, SeverityIcon.CssClass(null));

    // Pins the shared severity -> icon/colour class map. Every known level gets a DISTINCT Bootstrap-Icon shape (so
    // levels are legible without colour / in forced-colors); Critical/Error/Warning/Verbose add a colour class,
    // Information is icon-only, and null falls through to "" so no icon span is rendered.
    [Theory]
    [InlineData(SeverityLevel.Critical, "bi bi-exclamation-octagon-fill critical")]
    [InlineData(SeverityLevel.Error, "bi bi-exclamation-circle error")]
    [InlineData(SeverityLevel.Warning, "bi bi-exclamation-triangle warning")]
    [InlineData(SeverityLevel.Information, "bi bi-info-circle")]
    [InlineData(SeverityLevel.Verbose, "bi bi-circle verbose")]
    public void CssClass_ReturnsDistinctShapePerLevel(SeverityLevel level, string expected) =>
        Assert.Equal(expected, SeverityIcon.CssClass(level));
}
