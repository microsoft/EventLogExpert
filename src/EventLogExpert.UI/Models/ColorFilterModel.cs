// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.UI.Models;

public sealed record ColorFilterModel
{
    public FilterColors Color { get; set; } = FilterColors.None;

    public FilterComparison Comparison { get; set; } = new();
}
