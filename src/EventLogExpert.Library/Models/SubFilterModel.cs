// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Library.EventArgs;
using EventLogExpert.Library.Helpers;

namespace EventLogExpert.Library.Models;

public class SubFilterModel
{
    public SubFilterModel(FilterType filterType)
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
    }

    public Guid Id { get; set; } = Guid.NewGuid();

    public FilterComparison FilterComparison { get; set; }

    public dynamic? FilterValue { get; private set; }

    public void UpdateFilterValue(ValueChangedEventArgs args) => FilterValue = args.Value;
}
