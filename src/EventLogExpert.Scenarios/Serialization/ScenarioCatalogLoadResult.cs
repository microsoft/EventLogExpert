// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Scenarios.Catalog;
using System.Collections.Immutable;

namespace EventLogExpert.Scenarios.Serialization;

/// <summary>Outcome of a catalog load: the validated scenarios plus every error found.</summary>
internal sealed record ScenarioCatalogLoadResult(
    ImmutableList<ScenarioDefinition> Scenarios,
    ImmutableList<string> Errors);
