// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Text.Json.Serialization;

namespace EventLogExpert.Filtering;

[JsonConverter(typeof(SubFilterJsonConverter))]
public sealed record SubFilter(FilterComparison Comparison, bool JoinWithAny);
