// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Library.Helpers;

namespace EventLogExpert.Library.Models;

public class FilterModel
{
    public FilterModel(int id) => Id = id;

    public int Id { get; set; }

    public Func<DisplayEventModel, bool>? Comparison { get; set; }

    public string? ComparisonString { get; set; }

    public bool IsEditing { get; set; } = true;

    public FilterType FilterType { get; set; }

    public FilterComparison FilterComparison { get; set; }

    public string? FilterStringValue { get; set; }

    public int? FilterIntValue { get; set; }
}
