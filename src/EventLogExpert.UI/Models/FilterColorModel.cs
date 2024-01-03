// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.UI.Models;

public sealed record FilterColorModel
{
    public Guid Id { get; init; }

    public FilterColor Color { get; set; } = FilterColor.None;

    public FilterComparison Comparison { get; init; } = new();
}
