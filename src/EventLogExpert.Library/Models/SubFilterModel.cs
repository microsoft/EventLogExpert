// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Library.Helpers;

namespace EventLogExpert.Library.Models;

public class SubFilterModel
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public FilterComparison FilterComparison { get; set; }

    public int? FilterIntValue { get; set; }

    public SeverityLevel? FilterSeverityValue { get; set; }

    public string? FilterStringValue { get; set; }
}
