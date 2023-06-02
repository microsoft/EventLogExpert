// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Runtime.Serialization;

namespace EventLogExpert.Library.Helpers;

public enum SeverityLevel
{
    Error = 2,
    Warning = 3,
    Information = 4
}

public enum FilterType
{
    [EnumMember(Value = "Event ID")] EventId,
    Level,
    Keywords,
    Source,
    Task,
    Description
}

public enum FilterComparison
{
    Equals,
    Contains,
    [EnumMember(Value = "Not Equal")] NotEqual,
    [EnumMember(Value = "Not Contains")] NotContains
}
