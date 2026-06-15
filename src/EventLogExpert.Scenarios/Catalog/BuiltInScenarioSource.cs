// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Scenarios.Serialization;
using System.Collections.Immutable;

namespace EventLogExpert.Scenarios.Catalog;

/// <summary>The built-in source: scenarios embedded in this assembly, validated at load.</summary>
public sealed class BuiltInScenarioSource : IScenarioSource
{
    private readonly ImmutableList<ScenarioDefinition> _scenarios =
        ScenarioCatalogLoader.Load(typeof(BuiltInScenarioSource).Assembly);

    public IReadOnlyList<ScenarioDefinition> GetScenarios() => _scenarios;
}
