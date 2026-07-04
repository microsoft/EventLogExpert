// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.DatabaseTools.Common.Operations;
using EventLogExpert.Eventing.OfflineImaging.Wim;

namespace EventLogExpert.Runtime.DatabaseTools.Elevation;

public sealed record OfflineImageEditionsResult(
    DatabaseToolsOutcome Outcome,
    WimImageList? Editions,
    string? FailureSummary);
