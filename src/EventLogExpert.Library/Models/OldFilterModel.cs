// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Library.Helpers;

namespace EventLogExpert.Library.Models;

public class OldFilterModel
{
    // Set to -1 to indicate that the filter is not set
    public int Id { get; set; } = -1;

    public SeverityLevel? Level { get; set; }

    public string Provider { get; set; } = string.Empty;

    public string Task { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;
}
