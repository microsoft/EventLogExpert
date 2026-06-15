// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Scenarios.Common;
using System.Collections.Immutable;

namespace EventLogExpert.Scenarios.Catalog;

/// <summary>An immutable, validated built-in scenario: a named filter set tied to one or more channels.</summary>
public sealed record ScenarioDefinition
{
    /// <summary>Stable, kebab-case identity, unique across the catalog.</summary>
    public required string Id { get; init; }

    public required string Name { get; init; }

    public required string Purpose { get; init; }

    public required ScenarioGroup Group { get; init; }

    /// <summary>Required channels; more than one denotes a combined scenario.</summary>
    public required ImmutableArray<string> Channels { get; init; }

    /// <summary>Optional channels that never gate visibility.</summary>
    public ImmutableArray<string> OptionalChannels { get; init; } = [];

    public ScenarioGating Gating { get; init; } = ScenarioGating.ChannelPresence;

    /// <summary>Source/publisher names gated on for <see cref="ScenarioGating.SourceRegistration" />.</summary>
    public ImmutableArray<string> SourceGates { get; init; } = [];

    /// <summary>True when any required channel needs process elevation to read.</summary>
    public bool RequiresAdmin { get; init; }

    /// <summary>The ordered filter rows, each materialising to one Basic filter.</summary>
    public required ImmutableArray<ScenarioFilterRow> Filters { get; init; }

    /// <summary>0 = starter set, 1 = full catalog; lower sorts first.</summary>
    public int Priority { get; init; }

    public int Order { get; init; }

    public int Version { get; init; } = 1;

    public ScenarioOrigin Origin { get; init; } = ScenarioOrigin.BuiltIn;

    /// <summary>Deterministic name-based identity (RFC 4122 v5 over <see cref="Id" />).</summary>
    public Guid StableGuid => DeterministicGuid.Create(DeterministicGuid.ScenarioNamespace, Id);
}
