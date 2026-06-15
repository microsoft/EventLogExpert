// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Scenarios.Catalog;

/// <summary>Where a scenario came from.</summary>
public enum ScenarioOrigin
{
    /// <summary>Shipped with the app as an immutable embedded resource.</summary>
    BuiltIn,

    /// <summary>Contributed by an additive community or user pack.</summary>
    Pack
}
