// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Basic;
using EventLogExpert.Filtering.Persistence;

namespace EventLogExpert.Scenarios.Catalog;

/// <summary>One row of a scenario's filter set; maps to one Basic <c>SavedFilter</c>.</summary>
public sealed record ScenarioFilterRow(
    BasicFilter Filter,
    bool IsExcluded = false,
    HighlightColor Color = HighlightColor.None);
