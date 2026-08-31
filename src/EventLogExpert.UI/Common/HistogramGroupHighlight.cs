// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Persistence;

namespace EventLogExpert.UI.Common;

public readonly record struct HistogramGroupHighlight(string? CssName, HistogramHighlightKind Kind, HighlightColor? Color);
