// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Library.EventArgs;
using EventLogExpert.Library.Helpers;

namespace EventLogExpert.Library.Models;

public class FilterModel
{
    public FilterModel(Guid id)
    {
        Id = id;
        FilterType = FilterType.EventId;
    }

    public Guid Id { get; init; }

    public List<Func<DisplayEventModel, bool>> Comparison { get; set; } = new();

    public string? ComparisonString { get; set; }

    public bool IsEditing { get; set; } = true;

    public FilterType FilterType { get => _filterType; set => UpdateFilterType(value); }

    public FilterComparison FilterComparison { get; set; }

    public dynamic? FilterValue { get; private set; }

    public List<SubFilterModel> SubFilters { get; set; } = new();

    private FilterType _filterType;

    private void UpdateFilterType(FilterType filterType)
    {
        FilterValue = filterType switch
        {
            FilterType.EventId => default(int),
            FilterType.Level => SeverityLevel.All,
            FilterType.Source => string.Empty,
            FilterType.Task => string.Empty,
            FilterType.Description => string.Empty,
            _ => throw new Exception("Invalid Filter Type")
        };

        _filterType = filterType;
    }

    public void UpdateFilterValue(ValueChangedEventArgs args) => FilterValue = args.Value;
}
