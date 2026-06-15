// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Scenarios.Serialization;

/// <summary>Wire shape for the embedded scenario JSON; strictly parsed by the loader.</summary>
internal sealed class ScenarioFileDto
{
    public List<ScenarioDto>? Scenarios { get; set; }

    public int SchemaVersion { get; set; }
}
