// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using System.Collections.Frozen;

namespace EventLogExpert.Eventing.ProviderMetadata;

public static class LegacyMessageFilePaths
{
    private static readonly FrozenSet<string> s_supportedExtensions =
        new[] { ".dll", ".exe", ".sys" }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<string> GetSupportedModulePaths(string messageFileValue)
    {
        ArgumentNullException.ThrowIfNull(messageFileValue);

        return
        [
            .. messageFileValue
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(path => s_supportedExtensions.Contains(Path.GetExtension(path)))
        ];
    }
}
