// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Runtime.Serialization;

namespace EventLogExpert.UI.Filter;

public enum FilterEvaluator
{
    Equals,
    Contains,
    [EnumMember(Value = "Not Equal")] NotEqual,
    [EnumMember(Value = "Not Contains")] NotContains,
    [EnumMember(Value = "Multi Select")] MultiSelect
}
