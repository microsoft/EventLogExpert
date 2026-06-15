// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Scenarios.Serialization;

internal sealed class ComparisonDto
{
    public string? MatchMode { get; set; }

    public string? Operator { get; set; }

    public string? Property { get; set; }

    public string? Value { get; set; }

    public List<string>? Values { get; set; }
}
