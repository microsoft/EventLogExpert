// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
using Microsoft.Extensions.Localization;

namespace EventLogExpert.UI;

internal static class LocalizableTextResolver
{
    public static string Resolve(IStringLocalizer localizer, LocalizableText text) =>
        Resolve(localizer, text.Key, text.Args, text.Key);

    public static string Resolve(IStringLocalizer localizer, string? key, IReadOnlyList<string>? args, string english)
    {
        if (string.IsNullOrEmpty(key)) { return english; }

        try
        {
            var localized = localizer[key, [.. args ?? []]];

            return localized.ResourceNotFound ? english : localized.Value;
        }
        catch (FormatException)
        {
            return english;
        }
    }
}
