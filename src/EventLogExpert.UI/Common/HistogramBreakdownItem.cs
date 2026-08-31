// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Runtime.Histogram;

namespace EventLogExpert.UI.Common;

internal readonly record struct HistogramBreakdownItem(
    int Count,
    HistogramGroupLabel Label,
    string HighlightText);
