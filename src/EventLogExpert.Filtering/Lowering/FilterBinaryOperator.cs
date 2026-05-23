// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Filtering.Common.Filtering;

namespace EventLogExpert.Filtering.Lowering;

/// <summary>
///     Parser-side enumeration of every binary comparison operator the grammar accepts. Wider than
///     <see cref="ComparisonOperator" /> (which only enumerates the BasicFilter authoring vocabulary) because the Advanced
///     free-text grammar accepts <c>&gt;</c>, <c>&lt;</c>, <c>&gt;=</c>, <c>&lt;=</c> in addition to <c>==</c> / <c>!=</c>.
/// </summary>
internal enum FilterBinaryOperator
{
    Equal,
    NotEqual,
    GreaterThan,
    LessThan,
    GreaterThanOrEqual,
    LessThanOrEqual
}
