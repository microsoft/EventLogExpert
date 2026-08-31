// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Localization;
using Microsoft.Extensions.Localization;

namespace EventLogExpert.UI.Common;

internal static class HistogramHighlightFormatter
{
    internal static string Format(IStringLocalizer<SharedResource> localizer, HistogramGroupHighlight highlight) =>
        highlight.Kind switch
        {
            HistogramHighlightKind.Mixed => localizer["Histogram_Highlight_Mixed"],
            HistogramHighlightKind.Uncolored => localizer["Histogram_Highlight_Uncolored"],
            HistogramHighlightKind.Single => localizer["Histogram_Highlight_Single",
                HighlightColorLocalizer.Label(localizer, highlight.Color!.Value)],
            HistogramHighlightKind.None => string.Empty,
            _ => throw new ArgumentOutOfRangeException(nameof(highlight), highlight.Kind, null)
        };
}
