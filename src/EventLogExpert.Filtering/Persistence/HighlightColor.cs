// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Runtime.Serialization;

namespace EventLogExpert.Filtering.Persistence;

public enum HighlightColor
{
    None,
    [EnumMember(Value = "lightred")] LightRed,
    [EnumMember(Value = "red")] Red,
    [EnumMember(Value = "darkred")] DarkRed,
    [EnumMember(Value = "lightorange")] LightOrange,
    [EnumMember(Value = "orange")] Orange,
    [EnumMember(Value = "darkorange")] DarkOrange,
    [EnumMember(Value = "lightyellow")] LightYellow,
    [EnumMember(Value = "yellow")] Yellow,
    [EnumMember(Value = "darkyellow")] DarkYellow,
    [EnumMember(Value = "lightgreen")] LightGreen,
    [EnumMember(Value = "green")] Green,
    [EnumMember(Value = "darkgreen")] DarkGreen,
    [EnumMember(Value = "lightteal")] LightTeal,
    [EnumMember(Value = "teal")] Teal,
    [EnumMember(Value = "darkteal")] DarkTeal,
    [EnumMember(Value = "lightblue")] LightBlue,
    [EnumMember(Value = "blue")] Blue,
    [EnumMember(Value = "darkblue")] DarkBlue,
    [EnumMember(Value = "lightpurple")] LightPurple,
    [EnumMember(Value = "purple")] Purple,
    [EnumMember(Value = "darkpurple")] DarkPurple,
    [EnumMember(Value = "lightmagenta")] LightMagenta,
    [EnumMember(Value = "magenta")] Magenta,
    [EnumMember(Value = "darkmagenta")] DarkMagenta,
    [EnumMember(Value = "lightpink")] LightPink,
    [EnumMember(Value = "pink")] Pink,
    [EnumMember(Value = "darkpink")] DarkPink
}
