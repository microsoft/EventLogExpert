// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.UI.Models;

public sealed record FilterModel
{
    public Guid Id { get; } = Guid.NewGuid();

    public FilterComparison Comparison { get; set; } = new();

    public FilterData Data { get; set; } = new();

    public List<FilterModel> SubFilters { get; set; } = [];

    public bool IsEditing { get; set; }

    public bool IsEnabled { get; set; }
}
