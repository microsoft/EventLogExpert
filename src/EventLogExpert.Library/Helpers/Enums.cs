// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Runtime.Serialization;

namespace EventLogExpert.Library.Helpers;

public enum SeverityLevel
{
    All = -1,
    Error = 2,
    Warning = 3,
    Information = 4
}

public enum FilterType
{
    EventId,
    Severity,
    Provider,
    Task,
    Description
}

public enum FilterComparison
{
    Equals,
    Contains,
    [EnumMember(Value = "Not Equal")] NotEqual
}
