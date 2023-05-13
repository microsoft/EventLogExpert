// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Fluxor;

namespace EventLogExpert.Store.Settings;

[FeatureState]
public record SettingsState
{
    public int TimeZone { get; init; }

    public IEnumerable<string> LoadedProviders { get; init; } = Enumerable.Empty<string>();
}
