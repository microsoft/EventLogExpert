// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Runtime.Serialization;

namespace EventLogExpert.Filtering;

public enum ComparisonOperator
{
    Equals,
    Contains,
    [EnumMember(Value = "Not Equal")] NotEqual,
    [EnumMember(Value = "Not Contains")] NotContains
}
