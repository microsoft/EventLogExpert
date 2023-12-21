// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Models;
using System.Linq.Dynamic.Core;

namespace EventLogExpert.UI.Models;

public sealed record FilterModel
{
    private string _comparisonString = string.Empty;
    private FilterType _filterType;

    public Guid Id { get; } = Guid.NewGuid();

    public string ComparisonString
    {
        get => _comparisonString;
        set
        {
            _comparisonString = value;

            Comparison = DynamicExpressionParser
                .ParseLambda<DisplayEventModel, bool>(
                    EventLogExpertCustomTypeProvider.ParsingConfig,
                    false,
                    _comparisonString)
                .Compile();
        }
    }

    public Func<DisplayEventModel, bool> Comparison { get; private set; } = null!;

    public bool IsEditing { get; set; } = true;

    public bool IsEnabled { get; set; } = false;

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

    public List<string> FilterValues { get; set; } = [];

    public List<SubFilterModel> SubFilters { get; set; } = [];
}
