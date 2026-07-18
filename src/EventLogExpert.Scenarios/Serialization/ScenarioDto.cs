// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Scenarios.Serialization;

internal sealed class ScenarioDto
{
    public bool ActivatesTimeline { get; set; }

    public List<string>? Channels { get; set; }

    public List<ScenarioFilterRowDto>? Filters { get; set; }

    public string? Gating { get; set; }

    public string? Group { get; set; }

    public string? Id { get; set; }

    public string? Name { get; set; }

    public List<string>? OptionalChannels { get; set; }

    public int Order { get; set; }

    public string? Origin { get; set; }

    public int Priority { get; set; }

    public string? Purpose { get; set; }

    public bool RequiresAdmin { get; set; }

    public List<string>? SourceGates { get; set; }

    public string? TimelineDimension { get; set; }

    public int Version { get; set; }
}
