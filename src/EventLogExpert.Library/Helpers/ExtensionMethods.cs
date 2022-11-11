// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Reflection;
using System.Runtime.Serialization;
using System.Text.RegularExpressions;

namespace EventLogExpert.Library.Helpers;

public static class ExtensionMethods
{
    public static string ReplaceCaseInsensitiveFind(
        this string str,
        string findMe,
        string newValue
    )
    {
        return Regex.Replace(str,
            Regex.Escape(findMe),
            Regex.Replace(newValue, "\\$[0-9]+", @"$$$0"),
            RegexOptions.IgnoreCase);
    }

    public static string ToFullString(this Enum value)
    {
        var memberAttribute = value.GetType().GetField(value.ToString())?
            .GetCustomAttribute(typeof(EnumMemberAttribute)) as EnumMemberAttribute;

        return memberAttribute?.Value ?? value.ToString();
    }
}
