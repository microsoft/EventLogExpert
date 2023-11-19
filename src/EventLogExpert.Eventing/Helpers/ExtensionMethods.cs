// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Text.RegularExpressions;

namespace EventLogExpert.Eventing.Helpers;

public static partial class ExtensionMethods
{
    public static string ReplaceCaseInsensitiveFind(this string str, string findMe, string newValue) => Regex.Replace(
        str,
        Regex.Escape(findMe),
        ReplacementRegex().Replace(newValue, @"$$$0"),
        RegexOptions.IgnoreCase);

    [GeneratedRegex("\\$[0-9]+")]
    private static partial Regex ReplacementRegex();
}
