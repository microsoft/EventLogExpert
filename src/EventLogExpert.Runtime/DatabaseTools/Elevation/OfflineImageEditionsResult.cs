// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.DatabaseTools.Common.Operations;
using EventLogExpert.Eventing.OfflineImaging.Wim;
using EventLogExpert.Logging.Abstractions;

namespace EventLogExpert.Runtime.DatabaseTools.Elevation;

public sealed record OfflineImageEditionsResult(
    DatabaseToolsOutcome Outcome,
    WimImageList? Editions,
    LocalizableText? Summary)
{
    public bool SummaryIsDiagnostic { get; init; }

    public string? DiagnosticDetail { get; init; }
}
