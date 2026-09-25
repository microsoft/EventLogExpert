// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Logging.Abstractions;

public sealed record LocalizableText(string Key, IReadOnlyList<string> Args)
{
    /// <summary>Convenience constructor for a key with no format arguments.</summary>
    public LocalizableText(string key) : this(key, []) { }
}
