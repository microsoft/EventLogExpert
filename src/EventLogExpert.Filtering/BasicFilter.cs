// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Collections.Immutable;

namespace EventLogExpert.Filtering;

public sealed record BasicFilter(BasicFilterCondition Comparison, ImmutableList<SubFilter> SubFilters)
{
    public static BasicFilter Empty { get; } = new(new BasicFilterCondition(), []);
}
