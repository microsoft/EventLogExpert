// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Library.Models;
using Fluxor;
using System.Collections.Immutable;

namespace EventLogExpert.Store.Settings;

[FeatureState]
public record SettingsState
{
    public SettingsModel Config { get; init; } = new();

    public IImmutableList<string> LoadedProviders { get; init; } = ImmutableList<string>.Empty;

    public bool ShowLogName { get; init; }

    public bool ShowComputerName { get; init; }
}
