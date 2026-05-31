// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Text.Json.Serialization;

namespace EventLogExpert.Filtering.Basic;

[JsonConverter(typeof(FilterPredicateJsonConverter))]
public sealed record FilterPredicate(FilterComparison Comparison, bool JoinWithAny);
