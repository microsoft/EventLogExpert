// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Models;
using System.Linq.Dynamic.Core;

namespace EventLogExpert.UI.Models;

public record FilterCacheModel
{
    private string _comparisonString = string.Empty;

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

    public bool IsFavorite { get; set; }

    public bool IsEnabled { get; set; } = true;
}
