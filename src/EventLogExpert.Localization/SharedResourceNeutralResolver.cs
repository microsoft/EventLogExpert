// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Logging.Abstractions;
using System.Globalization;
using System.Resources;

namespace EventLogExpert.Localization;

public sealed class SharedResourceNeutralResolver : INeutralTextResolver
{
    private static readonly ResourceManager s_resourceManager = new(
        "EventLogExpert.Localization.Resources.SharedResource",
        typeof(SharedResource).Assembly);

    public string Resolve(string key, IReadOnlyList<string> args)
    {
        string template = s_resourceManager.GetString(key, CultureInfo.InvariantCulture) ?? key;

        if (args.Count == 0) { return template; }

        try
        {
            return string.Format(CultureInfo.InvariantCulture, template, [.. args]);
        }
        catch (FormatException)
        {
            // Best-effort: a malformed template or argument-count mismatch must never crash the logging pipeline.
            return template;
        }
    }
}
