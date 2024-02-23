﻿// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.UI.Models;
using Fluxor;
using System.Collections.Immutable;

namespace EventLogExpert.UI.Store.Settings;

[FeatureState]
public record SettingsState
{
    public SettingsModel Config { get; init; } = new();

    public IImmutableList<string> DisabledDatabases { get; init; } = ImmutableList<string>.Empty;

    public IImmutableList<string> LoadedDatabases { get; init; } = ImmutableList<string>.Empty;
}
