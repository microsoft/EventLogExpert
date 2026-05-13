// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Filtering;

/// <summary>
///     Whether a <see cref="BasicFilterCondition" /> compares against a single value (
///     <see cref="BasicFilterCondition.Value" />) or against a set of values (<see cref="BasicFilterCondition.Values" />).
///     Combined with <see cref="ComparisonOperator" /> this is the canonical replacement for the legacy
///     <c>FilterEvaluator.MultiSelect</c> entry, which was simply <c>(Equals, Many)</c>.
/// </summary>
public enum MatchMode
{
    Single,
    Many
}
