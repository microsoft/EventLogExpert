// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Collections.Immutable;

namespace EventLogExpert.UI.Models;

public sealed record BasicFilter(FilterData Comparison, ImmutableList<SubFilter> SubFilters)
{
    public static BasicFilter Empty { get; } = new(new FilterData(), []);
}
