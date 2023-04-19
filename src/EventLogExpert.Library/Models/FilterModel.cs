// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Library.Helpers;

namespace EventLogExpert.Library.Models;

public class FilterModel
{
    public FilterModel(Guid id) => Id = id;

    public Guid Id { get; set; }

    public List<Func<DisplayEventModel, bool>> Comparison { get; set; } = new();

    public string? ComparisonString { get; set; }

    public bool IsEditing { get; set; } = true;

    public FilterType FilterType { get; set; }

    public FilterComparison FilterComparison { get; set; }

    // TODO: Find a better way to do this
    // (object?) does not work, gets set as a string from blazor select component so SeverityLevel cast fails on get call

    public int? FilterIntValue { get; set; }

    public SeverityLevel? FilterSeverityValue { get; set; }

    public string? FilterStringValue { get; set; }

    public List<SubFilterModel> SubFilters { get; set; } = new();
}
