// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.UI.Models;

public sealed record FilterGroupModel
{
    public string Name { get; set; } = string.Empty;

    public IEnumerable<string> Filters { get; set; } = [];
}
