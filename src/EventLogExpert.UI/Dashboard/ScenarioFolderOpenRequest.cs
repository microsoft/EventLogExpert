// // Copyright (c) Microsoft Corporation.
// // Licensed under the MIT License.

using EventLogExpert.Scenarios.Catalog;

namespace EventLogExpert.UI.Dashboard;

public readonly record struct ScenarioFolderOpenRequest(ScenarioDefinition Scenario, bool IncludeSubfolders);
