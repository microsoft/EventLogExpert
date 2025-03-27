// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Eventing.Models;
using System.Linq.Dynamic.Core;
using System.Text.Json.Serialization;

namespace EventLogExpert.UI.Models;

public sealed record FilterComparison
{
    private string _value = string.Empty;

    public string Value
    {
        get => _value;
        set
        {
            Expression = DynamicExpressionParser
                .ParseLambda<DisplayEventModel, bool>(new ParsingConfig { AllowEqualsAndToStringMethodsOnObject = true }, false, value)
                .Compile();

            _value = value;
        }
    }

    [JsonIgnore]
    public Func<DisplayEventModel, bool> Expression { get; private set; } = null!;
}
