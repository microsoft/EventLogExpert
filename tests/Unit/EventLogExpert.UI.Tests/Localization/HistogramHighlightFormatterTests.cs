// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Persistence;
using EventLogExpert.UI.Common;
using EventLogExpert.UI.Tests.TestUtils;

namespace EventLogExpert.UI.Tests.Localization;

public sealed class HistogramHighlightFormatterTests
{
    private readonly MarkerLocalizer _localizer = new();

    [Theory]
    [InlineData(HistogramHighlightKind.Mixed, null, "[[Histogram_Highlight_Mixed]]")]
    [InlineData(HistogramHighlightKind.Uncolored, null, "[[Histogram_Highlight_Uncolored]]")]
    [InlineData(HistogramHighlightKind.Single, HighlightColor.LightRed, "[[Histogram_Highlight_Single([[Histogram_HighlightColor_LightRed]])]]")]
    [InlineData(HistogramHighlightKind.None, null, "")]
    public void Format_RoutesEveryHighlightKindToItsLocalizedDescription(
        HistogramHighlightKind kind,
        HighlightColor? color,
        string expected)
    {
        Assert.Equal(expected, HistogramHighlightFormatter.Format(_localizer, new HistogramGroupHighlight(null, kind, color)));
    }
}
