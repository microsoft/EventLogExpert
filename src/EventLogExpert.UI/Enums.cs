// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Runtime.Serialization;

namespace EventLogExpert.UI;

public enum FilterType
{
    [EnumMember(Value = "Event ID")] Id,
    [EnumMember(Value = "Activity ID")] ActivityId,
    Level,
    [EnumMember(Value = "Keywords")] KeywordsDisplayNames,
    Source,
    [EnumMember(Value = "Task Category")] TaskCategory,
    Description
}

public enum FilterComparison
{
    Equals,
    Contains,
    [EnumMember(Value = "Not Equal")] NotEqual,
    [EnumMember(Value = "Not Contains")] NotContains,
    [EnumMember(Value = "Multi Select")] MultiSelect
}

public enum CacheType
{
    Favorites,
    Recent
}

public enum CopyType
{
    Full,
    Simple,
    Xml
}

public enum ColumnName
{
    Level,
    [EnumMember(Value = "Date and Time")] DateAndTime,
    [EnumMember(Value = "Activity ID")] ActivityId,
    [EnumMember(Value = "Log Name")] LogName,
    [EnumMember(Value = "Computer Name")] ComputerName,
    Source,
    [EnumMember(Value = "Event ID")] EventId,
    [EnumMember(Value = "Task Category")] TaskCategory
}
