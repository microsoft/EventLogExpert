// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Helpers;

namespace EventLogExpert.UI.Models;

public record SubFilterModel
{
    private FilterType _filterType;

    public Guid Id { get; } = Guid.NewGuid();

    public FilterType FilterType
    {
        get => _filterType;
        set
        {
            _filterType = value;
            FilterValue = null;
            FilterValues.Clear();
        }
    }

    public FilterComparison FilterComparison { get; set; }

    public string? FilterValue { get; set; }

    public List<string> FilterValues { get; set; } = new();
}
