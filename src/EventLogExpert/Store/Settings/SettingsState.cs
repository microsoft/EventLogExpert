// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using Fluxor;

namespace EventLogExpert.Store.Settings;

[FeatureState]
public record SettingsState
{
    public string TimeZoneId { get; init; } = string.Empty;

    public TimeZoneInfo TimeZone { get; init; } = null!;

    public bool IsPrereleaseEnabled { get; init; }

    public IEnumerable<string> LoadedProviders { get; init; } = Enumerable.Empty<string>();
}
