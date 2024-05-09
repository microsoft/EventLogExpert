// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.UI.Models;

public sealed record FilterData
{
    private FilterCategory _category;

    public FilterCategory Category
    {
        get => _category;
        set
        {
            _category = value;
            Value = null;
            Values.Clear();
        }
    }

    public FilterEvaluator Evaluator { get; set; }

    public string? Value { get; set; }

    public List<string> Values { get; set; } = [];
}
