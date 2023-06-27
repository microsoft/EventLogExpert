// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Runtime.Serialization;

namespace EventLogExpert.UI;

public enum FilterType
{
    [EnumMember(Value = "Event ID")] Id,
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
    [EnumMember(Value = "Not Contains")] NotContains
}

public enum CacheType
{
    Favorites,
    Recent
}
