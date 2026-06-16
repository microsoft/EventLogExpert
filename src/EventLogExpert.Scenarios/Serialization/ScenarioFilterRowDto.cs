// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Scenarios.Serialization;

internal sealed class ScenarioFilterRowDto
{
    public string? Color { get; set; }

    public ComparisonDto? Comparison { get; set; }

    public bool? IsExcluded { get; set; }

    public List<PredicateDto>? Predicates { get; set; }
}
