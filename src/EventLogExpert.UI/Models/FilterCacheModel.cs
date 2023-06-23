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
            try
            {
                Comparison = DynamicExpressionParser
                    .ParseLambda<DisplayEventModel, bool>(
                        EventLogExpertCustomTypeProvider.ParsingConfig,
                        false,
                        value)
                    .Compile();

                _comparisonString = value;
            }
            catch
            { // TODO: Int.Contains works for filtering but not dynamic linq
            }
        }
    }

    public Func<DisplayEventModel, bool> Comparison { get; private set; } = null!;

    public bool IsEnabled { get; set; } = true;
}
