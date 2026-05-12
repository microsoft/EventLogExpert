// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Collections.Immutable;

namespace EventLogExpert.UI.Filter;

public sealed record BasicFilter(FilterCondition Comparison, ImmutableList<SubFilter> SubFilters)
{
    public static BasicFilter Empty { get; } = new(new FilterCondition(), []);
}
