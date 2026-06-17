// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

namespace EventLogExpert.Runtime.Scenarios;

/// <summary>
///     Gates the dev-only scenario authoring / export UI. Enabled in DEBUG builds, or in RELEASE when the
///     <c>/EnableScenarioAuthoring</c> command-line switch is passed (set in <c>MauiProgram</c>).
/// </summary>
public sealed record ScenarioAuthoringOptions(bool Enabled);
