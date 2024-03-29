﻿// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Text.RegularExpressions;

namespace EventLogExpert.Eventing.Helpers;

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
}
