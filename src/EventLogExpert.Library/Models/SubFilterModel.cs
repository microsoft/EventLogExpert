// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Library.Helpers;

namespace EventLogExpert.Library.Models;

public record SubFilterModel
{
    private FilterType _filterType;

    public Guid Id { get; set; } = Guid.NewGuid();

    public FilterType FilterType
    {
        get => _filterType;
        set
        {
            _filterType = value;
            FilterValue = string.Empty;
        }
    }

    public FilterComparison FilterComparison { get; set; }

    public string FilterValue { get; set; } = string.Empty;
}
