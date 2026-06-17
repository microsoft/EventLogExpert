// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Collections.Immutable;

namespace EventLogExpert.Scenarios.Catalog;

public sealed record ScenarioExportResult(string Json, ImmutableList<string> Warnings, int EmittedRowCount);
