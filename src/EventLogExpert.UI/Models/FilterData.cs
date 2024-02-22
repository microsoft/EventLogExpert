// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.UI.Models;

public sealed record FilterData
{
    private FilterType _type;

    public FilterType Type
    {
        get => _type;
        set
        {
            _type = value;
            Value = null;
            Values.Clear();
        }
    }

    public FilterEvaluator Evaluator { get; set; }

    public string? Value { get; set; }

    public List<string> Values { get; set; } = [];
}
