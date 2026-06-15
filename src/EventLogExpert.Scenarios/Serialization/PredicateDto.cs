// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Scenarios.Serialization;

internal sealed class PredicateDto
{
    public ComparisonDto? Comparison { get; set; }

    public bool JoinWithAny { get; set; }
}
