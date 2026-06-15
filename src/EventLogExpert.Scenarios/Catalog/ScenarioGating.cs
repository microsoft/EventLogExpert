// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Scenarios.Catalog;

/// <summary>How a scenario decides whether it is present (and therefore surfaced) on a given machine.</summary>
public enum ScenarioGating
{
    /// <summary>Hide unless the scenario's dedicated channel(s) exist in the host's log-name set.</summary>
    ChannelPresence,

    /// <summary>Hide unless the product's Source/publisher is registered on the always-present Application log.</summary>
    SourceRegistration
}
