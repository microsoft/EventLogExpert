// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.FilterLibrary;

public interface ILegacyPreferences
{
    bool ContainsKey(string key);

    string? GetString(string key);

    /// <summary>Removes <paramref name="key" /> from the persistent store; no-op when the key is absent.</summary>
    void Remove(string key);

    void SetString(string key, string value);
}
