// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Runtime.Serialization;

namespace EventLogExpert.UI;

public enum CacheType
{
    Favorites,
    Recent
}

public enum ColumnName
{
    Level,
    [EnumMember(Value = "Date and Time")] DateAndTime,
    [EnumMember(Value = "Activity ID")] ActivityId,
    Log,
    [EnumMember(Value = "Computer Name")] ComputerName,
    Source,
    [EnumMember(Value = "Event ID")] EventId,
    [EnumMember(Value = "Task Category")] TaskCategory,
    Keywords,
    [EnumMember(Value = "Process ID")] ProcessId,
    [EnumMember(Value = "Thread ID")] ThreadId,
    User
}

public enum CopyType
{
    Full,
    Simple,
    Xml
}

// Color samples: http://flounder.com/csharp_color_table.htm
public enum HighlightColor
{
    None,
    Blue,
    Green,
    Red,
    Yellow,
    Purple,
    Orange,
    Lime,
    Fuchsia,
    Aqua,
    DodgerBlue,
    YellowGreen,
    Tan,
    Firebrick,
    Violet,
    LightCoral,
    LightGray
}

public enum FilterCategory
{
    [EnumMember(Value = "Event ID")] Id,
    [EnumMember(Value = "Activity ID")] ActivityId,
    Level,
    [EnumMember(Value = "Keywords")] KeywordsDisplayNames,
    Source,
    [EnumMember(Value = "Task Category")] TaskCategory,
    Description,
    Xml
}

public enum FilterEvaluator
{
    Equals,
    Contains,
    [EnumMember(Value = "Not Equal")] NotEqual,
    [EnumMember(Value = "Not Contains")] NotContains,
    [EnumMember(Value = "Multi Select")] MultiSelect
}

public enum FilterType
{
    Basic,
    Advanced,
    Cached
}

public enum LogType
{
    Live,
    File
}
